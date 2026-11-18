#!/bin/sh
set -eu
password=$(cat /run/secrets/db_password)
case "$password" in ''|*[!a-f0-9]*) echo 'Invalid database secret' >&2; exit 2;; esac
[ ${#password} -eq 64 ] || exit 2
export CATALOG_CONNECTION="Host=127.0.0.1;Port=55439;Database=catalog_search;Username=catalog;Password=$password"
export ConnectionStrings__CatalogSearch="$CATALOG_CONNECTION"
unset password
if [ "${1:-}" = web ]; then
 exec dotnet /app/web/CatalogSearch.Web.dll
fi
exec dotnet /app/cli/CatalogSearch.ImportCli.dll "$@"
