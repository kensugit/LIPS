#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"
[[ $(docker info --format '{{.OSType}}') == linux ]] || { echo 'Linux Docker engine required' >&2; exit 2; }
[[ -z $(git status --porcelain) ]] || { echo 'Commit changes before building a release' >&2; exit 2; }
commit=$(git rev-parse HEAD)
out="$root/artifacts/docker-$commit"
[[ ! -e "$out" ]] || { echo 'Output already exists; use another clean commit' >&2; exit 2; }
mkdir -p "$out"
docker build --platform linux/amd64 -f deployment/Dockerfile -t "catalog-poc:$commit" .
pgref=${1:-postgres:17-bookworm}
if [[ "$pgref" != sha256:* ]]; then docker pull --platform linux/amd64 "$pgref"; fi
app=$(docker image inspect --format '{{.Id}}' "catalog-poc:$commit")
pg=$(docker image inspect --format '{{.Id}}' "$pgref")
docker save -o "$out/images.tar" "catalog-poc:$commit" "$pgref"
cp deployment/compose.yaml "$out/compose.yaml"
schema=$(find src/CatalogSearch.Infrastructure/Migrations DB/Migrations -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum | cut -d' ' -f1)
printf 'CATALOG_IMAGE=%s\nPOSTGRES_IMAGE=%s\nCATALOG_COMMIT=%s\nSCHEMA_SHA256=%s\n' "$app" "$pg" "$commit" "$schema" > "$out/release.env"
docker compose --env-file "$out/release.env" -f "$out/compose.yaml" config --quiet
tar -cf "$out/package.tar" -C "$out" images.tar compose.yaml release.env
sha256sum "$out/package.tar" > "$out/package.sha256"
printf 'Built (not deployed): %s\n' "$out/package.tar"
