param([Parameter(Mandatory)][string]$Workbook)
$ErrorActionPreference = 'Stop'
if (-not $env:CATALOG_TEST_CONNECTION -or -not $env:CATALOG_ACCEPTANCE_CONNECTION) { throw 'Set isolated CATALOG_TEST_CONNECTION and CATALOG_ACCEPTANCE_CONNECTION databases.' }
$env:CATALOG_WORKBOOK = (Resolve-Path -LiteralPath $Workbook).Path
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet restore
    if ($LASTEXITCODE) { throw 'restore failed' }
    dotnet build --no-restore
    if ($LASTEXITCODE) { throw 'build failed' }
    $env:CATALOG_CONNECTION = $env:CATALOG_ACCEPTANCE_CONNECTION
    dotnet run --no-build --project src/CatalogSearch.ImportCli -- migrate
    if ($LASTEXITCODE) { throw 'migration failed' }
    dotnet test --no-build --logger 'trx;LogFileName=persistence.trx' --results-directory artifacts/tests
    if ($LASTEXITCODE) { throw 'tests failed' }
    dotnet ef migrations has-pending-model-changes --project src/CatalogSearch.Infrastructure --no-build
    if ($LASTEXITCODE) { throw 'migration/model mismatch' }
} finally { Pop-Location }
