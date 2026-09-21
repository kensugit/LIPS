#!/usr/bin/env bash
set -euo pipefail
[[ $(id -u) == 0 ]] || { echo 'Run as root on Dell Ubuntu' >&2; exit 1; }
sha=0121e0b46151af35d2b3484359a9d92845c74cd553d51e1317bfe11c81ae7320
package=/home/catalogdeploy/incoming/$sha.tar
printf '%s  %s\n' "$sha" "$package" | sha256sum --check --quiet
stage=$(mktemp -d /var/tmp/lips-toyotsu-202609.XXXXXX)
chmod 700 "$stage"
tar --extract --file "$package" --directory "$stage" --no-same-owner
# The Web image runs as a non-root user. Keep the import private, but
# allow that exact user to read the bind-mounted files.
image=$(docker inspect --format '{{.Image}}' catalog-web-1)
[[ $image == sha256:574e372723b8989eb57f70b9faa9a75d9f22f6dc8e367a728f66a563787d57cb ]] || { echo 'Production image changed' >&2; exit 1; }
identity=$(docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges --entrypoint /bin/sh "$image" -c 'printf "%s:%s" "$(id -u)" "$(id -g)"')
[[ $identity =~ ^[0-9]+:[0-9]+$ ]] || { echo 'Invalid container UID/GID' >&2; exit 1; }
chown -R -- "$identity" "$stage"
find "$stage" -type d -exec chmod 500 {} +
find "$stage" -type f -exec chmod 400 {} +
# Test readability inside the same image before any database operation.
docker run --rm --network none --read-only --cap-drop ALL --security-opt no-new-privileges \
  --mount "type=bind,src=$stage,dst=/import,readonly" --entrypoint /bin/sh "$image" -c \
  'test -r /import/runtime/CatalogPdfBridge.dll && test -r /import/data/toyotsu.catalog.json && test -r /import/data/toyotsu.pdf'
echo 'Container read access verified.'
bash "$stage/import.sh" --check
bash "$stage/import.sh" --apply
