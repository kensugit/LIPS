#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/../deployment/catalogctl"
tmp=$(mktemp -d)
ROOT="$tmp/root"; STATE="$tmp/state"
mkdir -p "$ROOT/releases" "$STATE/approved" "$STATE/backups"
a=$(printf 'a%.0s' {1..64}); b=$(printf 'b%.0s' {1..64}); c=$(printf 'c%.0s' {1..64})
count=0
pass(){ count=$((count+1)); echo "PASS $1"; }
must_fail(){ if "$@"; then echo "Unexpected success: $*" >&2; exit 1; fi; }
must_fail hash_ok '../escape'; pass 'path traversal SHA rejected'
must_fail stage "$a"; pass 'unapproved package rejected'
printf bad > "$STATE/approved/$a.tar"
must_fail stage "$a"; pass 'tampered approved package rejected'
must_fail deploy "$b"; pass 'first installation not remotely automatic'
for sha in "$a" "$b"; do mkdir -p "$ROOT/releases/$sha"; printf 'CATALOG_IMAGE=sha256:%s\nPOSTGRES_IMAGE=sha256:%s\nCATALOG_COMMIT=%s\nSCHEMA_SHA256=%s\n' "$sha" "$c" "${a:0:40}" "$c" > "$ROOT/releases/$sha/release.env"; done
printf '%s\n' "$a" > "$STATE/current"
stage(){ printf '%s/releases/%s\n' "$ROOT" "$1"; }
log="$tmp/calls"
db_ready=1; health_ready=1
compose(){ echo "$*" >> "$log"; if [[ "$*" == *schema-check* && "$db_ready" == 0 ]]; then return 3; fi; }
backup(){ echo backup >> "$log"; }
healthy(){ [[ "$health_ready" == 1 || "$1" == "$ROOT/releases/$a" ]]; }
db_ready=0
must_fail deploy "$b"; [[ $(cat "$STATE/current") == "$a" ]]; ! grep -q 'up -d' "$log"; pass 'pending/unknown schema blocks before Web replacement'
db_ready=1
sed -i "s/POSTGRES_IMAGE=sha256:$c/POSTGRES_IMAGE=sha256:$a/" "$ROOT/releases/$b/release.env"
must_fail deploy "$b"; pass 'DB image change refused'
sed -i "s/POSTGRES_IMAGE=sha256:$a/POSTGRES_IMAGE=sha256:$c/" "$ROOT/releases/$b/release.env"
sed -i "s/SCHEMA_SHA256=$c/SCHEMA_SHA256=$a/" "$ROOT/releases/$b/release.env"
must_fail deploy "$b"; pass 'changed migration files refused even with matching history'
sed -i "s/SCHEMA_SHA256=$a/SCHEMA_SHA256=$c/" "$ROOT/releases/$b/release.env"
health_ready=0
must_fail deploy "$b"; [[ $(cat "$STATE/current") == "$a" ]]; grep -q "$ROOT/releases/$a up -d --no-deps web" "$log"; pass 'failed health restores prior Web and keeps current marker'
health_ready=1
deploy "$b"; [[ $(cat "$STATE/current") == "$b" && $(cat "$STATE/previous") == "$a" ]]; pass 'successful update records exact current/previous package'
must_fail deploy "$b"; pass 'same release rejected'
echo "passed=$count"
