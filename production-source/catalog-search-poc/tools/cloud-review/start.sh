#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
: "${CATALOG_CONNECTION:?Set a dedicated synthetic PostgreSQL connection string}"
case "$CATALOG_CONNECTION" in *'Database=lips_cloud_review;'*) ;; *) echo 'Use only Database=lips_cloud_review in this helper.' >&2; exit 2;; esac
: "${LIPS_PSQL_DSN:?Set psql connection for the SAME dedicated lips_cloud_review DB}"
[[ "$(psql "$LIPS_PSQL_DSN" -Atc 'select current_database()')" == 'lips_cloud_review' ]] || exit 2
dotnet restore CatalogSearch.slnx
dotnet build CatalogSearch.slnx --no-restore
dotnet run --no-build --project src/CatalogSearch.ImportCli -- migrate
psql "$LIPS_PSQL_DSN" -v ON_ERROR_STOP=1 -f DB/Migrations/20261012010000_IntakeReview.sql
export ConnectionStrings__CatalogSearch="$CATALOG_CONNECTION"
export CatalogDemo__Enabled=true CatalogDemo__WatchEnabled=false IntakeReview__Enabled=true
export ASPNETCORE_ENVIRONMENT=Development
export OriginalRender__Python="$(command -v python3)"
export OriginalRender__Cache="$PWD/review-render-cache"
dotnet run --no-build --no-launch-profile --project src/CatalogSearch.Web -- --urls http://127.0.0.1:56002
