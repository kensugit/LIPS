#!/usr/bin/env bash
# Build and test only. Production switching remains catalogctl's responsibility.
set -euo pipefail
[[ $EUID == 0 ]] || { echo 'Run with sudo on Dell Ubuntu-24.04.' >&2; exit 1; }
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
root=$(pwd -P)
sha256sum --check --quiet SHA256SUMS
expected_package=33459fd747b89872af1622fc1ba8f61de3fad9947e7dc1c6f13e34b65f9ee30c
expected_web=sha256:cbfa10b30124e407baf7c58bfdfe1b3d780b8e1ab81752c0a458e99066c66b95
expected_pg=sha256:051f7b7b3abdd564d5d1bd1e8c4b9c1b6e77087d1dd22020ede611c096a272e0
state=/var/lib/catalog-deploy
exec 9> "$state/deployment.lock"
flock -n 9 || { echo 'Another deployment is running.' >&2; exit 1; }
[[ $(cat "$state/current") == "$expected_package" ]] || { echo 'Production package changed; stop for compatibility review.' >&2; exit 1; }
[[ $(docker inspect --format '{{.Image}}' catalog-web-1) == "$expected_web" ]]
[[ $(docker inspect --format '{{.Image}}' catalog-postgres-1) == "$expected_pg" ]]
for container in catalog-web-1 catalog-postgres-1; do
  [[ $(docker inspect --format '{{.State.Health.Status}}' "$container") == healthy ]]
done
current="/opt/catalog/releases/$expected_package"
value() { sed -n "s/^$1=//p" "$current/release.env"; }
[[ $(value POSTGRES_IMAGE) == "$expected_pg" ]]
[[ $(value SCHEMA_SHA256) == "$(cat schema.sha256)" ]] || {
  echo 'Schema fingerprint differs; stop without changes.' >&2
  printf 'Current:  %s\nExpected: %s\n' "$(value SCHEMA_SHA256)" "$(cat schema.sha256)" >&2
  exit 1
}
[[ $(docker info --format '{{.OSType}}/{{.Architecture}}') == linux/x86_64 ]]
[[ -z $(ss -ltnH 'sport = :55443') ]] || { echo 'Preview port 55443 is busy.' >&2; exit 1; }
commit=$(cat source-commit.txt)
[[ $commit =~ ^[0-9a-f]{40}$ ]]
base_tag=lips-natural-base:cbfa10b30124
candidate="lips-natural-search:$commit"
docker tag "$expected_web" "$base_tag"
docker build --pull=false --network=none -t "$candidate" -f Dockerfile .
image=$(docker image inspect --format '{{.Id}}' "$candidate")
# Keep the existing CLI, entrypoint, user, health policy and runtime image.
docker run --rm --network host --read-only --cap-drop ALL --security-opt no-new-privileges \
  --mount type=bind,src=/etc/catalog/secrets/db_password,dst=/run/secrets/db_password,readonly \
  "$candidate" schema-check
preview="lips-natural-preview-$$"
cleanup() { docker rm -f "$preview" >/dev/null 2>&1 || true; }
trap cleanup EXIT
docker run -d --name "$preview" --network host --read-only --cap-drop ALL --security-opt no-new-privileges \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --mount type=bind,src=/etc/catalog/secrets/db_password,dst=/run/secrets/db_password,readonly \
  -e CatalogDemo__Enabled=true -e CatalogDemo__WatchEnabled=false --entrypoint /bin/sh "$candidate" -c '
    set -eu
    password=$(cat /run/secrets/db_password)
    case "$password" in ""|*[!a-f0-9]*) exit 2;; esac
    export ConnectionStrings__CatalogSearch="Host=127.0.0.1;Port=55439;Database=catalog_search;Username=catalog;Password=$password;Options=-c default_transaction_read_only=on"
    unset password
    exec dotnet /app/web/CatalogSearch.Web.dll --urls http://127.0.0.1:55443
  ' >/dev/null
for attempt in {1..30}; do
  if curl --fail --silent http://127.0.0.1:55443/health >/dev/null; then break; fi
  sleep 1
done
python3 verify-production.py | tee verification.json
cleanup
trap - EXIT
[[ $(cat "$state/current") == "$expected_package" ]]
out="$root/package"
mkdir -p "$out"
docker save -o "$out/images.tar" "$candidate"
cp "$current/compose.yaml" "$out/compose.yaml"
printf 'CATALOG_IMAGE=%s\nPOSTGRES_IMAGE=%s\nCATALOG_COMMIT=%s\nSCHEMA_SHA256=%s\n' \
  "$image" "$expected_pg" "$commit" "$(value SCHEMA_SHA256)" > "$out/release.env"
docker compose --env-file "$out/release.env" -f "$out/compose.yaml" config --quiet
tar -cf "$out/package.tar" -C "$out" images.tar compose.yaml release.env
sha=$(sha256sum "$out/package.tar" | cut -d' ' -f1)
install -m 0600 "$out/package.tar" "/home/catalogdeploy/incoming/$sha.tar"
printf '%s\n' "$sha" | tee "$out/package.sha256"
printf '\nBuild and preview passed; production is unchanged.\nReview verification.json, then register this exact package:\nsudo /usr/local/sbin/catalogctl approve %s\nReturn this SHA256 to Codex for deployment.\n' "$sha"
