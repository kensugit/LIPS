#!/usr/bin/env bash
# Run inside Dell Ubuntu-24.04. Default is read-only validation.
set -euo pipefail
mode=${1:---check}
[[ $# -le 1 && ( $mode == --check || $mode == --apply ) ]] || { echo 'Usage: bash import.sh [--check|--apply]' >&2; exit 2; }
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
root=$(pwd -P)
sha256sum --check --quiet SHA256SUMS
[[ $(id -u) == 0 ]] || { echo "Run as root on Dell Ubuntu" >&2; exit 1; }
for container in catalog-web-1 catalog-postgres-1; do
  [[ $(docker inspect --format '{{.State.Health.Status}}' "$container") == healthy ]] || { echo "$container is not healthy" >&2; exit 1; }
done
image=$(docker inspect --format '{{.Image}}' catalog-web-1)
[[ $image == sha256:e16969fe25c1b74c86947610f74a3d5847fd3c10da4c1e12374f5e6ff0714db0 ]] || { echo 'Production image changed; review compatibility before applying.' >&2; exit 1; }
[[ $(docker port catalog-postgres-1 5432/tcp) == 127.0.0.1:55439 ]] || { echo 'Unexpected database port' >&2; exit 1; }
exec 9> /tmp/lips-pdf-import.lock
flock -n 9 || { echo 'Another PDF import is running' >&2; exit 1; }
run_bridge() {
  docker run --rm --network host --read-only --cap-drop ALL --security-opt no-new-privileges \
    --mount "type=bind,src=$root,dst=/import,readonly" \
    --mount type=bind,src=/etc/catalog/secrets/db_password,dst=/run/secrets/db_password,readonly \
    --entrypoint /bin/sh "$image" -c '
      set -eu
      password=$(cat /run/secrets/db_password)
      case "$password" in ""|*[!a-zA-Z0-9]*) echo "Unsupported secret format; use a connection builder" >&2; exit 1;; esac
      export CATALOG_CONNECTION="Host=127.0.0.1;Port=55439;Database=catalog_search;Username=catalog;Password=$password"
      exec dotnet /import/runtime/CatalogPdfBridge.dll "$@"
    ' sh "/import/data/$1.catalog.json" "/import/data/$1.pdf" "$2"
}
for supplier in diony-news diony-catalog; do
  run_bridge "$supplier" --check-schema
done
if [[ $mode == --check ]]; then
  echo 'Preflight passed. No database changes. Run with --apply to import 516 Diony products.'
  exit 0
fi
backup="/var/lib/catalog-deploy/backups/before-diony-$(date +%Y%m%d-%H%M%S)-$$.dump"
sudo install -d -m 0700 /var/lib/catalog-deploy/backups
sudo bash -c '
  set -euo pipefail
  umask 077
  docker exec catalog-postgres-1 pg_dump -U catalog -d catalog_search -Fc > "$1"
  docker exec -i catalog-postgres-1 pg_restore -l < "$1" > /dev/null
' bash "$backup"
echo "Backup verified: $backup"
for supplier in diony-news diony-catalog; do
  run_bridge "$supplier" --persist
  run_bridge "$supplier" --verify
done
run_bridge diony-news --verify
curl --fail --max-time 15 http://127.0.0.1:55440/api/options
printf '\nDiony 516 products imported and verified.\n'
