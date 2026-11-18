param([Parameter(Mandatory)][string]$PgBin,[Parameter(Mandatory)][string]$PgData,[Parameter(Mandatory)][string]$WorkbookPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
if(Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue){throw 'Existing PostgreSQL process; inspect first'}
& "$pgBin\pg_ctl.exe" -D $pgData -l "$root\artifacts\docker-gate-postgres.log" -o '-h 127.0.0.1 -p 55439' start
if($LASTEXITCODE){throw 'PG start failed'}
try{
 $database='catalog_gate_'+[guid]::NewGuid().ToString('N')
 & "$pgBin\createdb.exe" -h 127.0.0.1 -p 55439 -U catalog $database
 if($LASTEXITCODE){throw 'Test database creation failed'}
 $env:CATALOG_CONNECTION="Host=127.0.0.1;Port=55439;Database=$database;Username=catalog"
 $cli='src/CatalogSearch.ImportCli/bin/Debug/net10.0/CatalogSearch.ImportCli.dll'
 $checks=@()
 dotnet $cli schema-check
 if($LASTEXITCODE -ne 3){throw 'Empty schema must be refused'}
 $checks+='empty schema refused'
 dotnet $cli migrate
 if($LASTEXITCODE){throw 'Fixture migration failed'}
 dotnet $cli schema-check
 if($LASTEXITCODE){throw 'Current schema should pass'}
 $checks+='matching schema accepted'
 $sql='INSERT INTO "__EFMigrationsHistory" ("MigrationId","ProductVersion") VALUES (''20990101000000_Unknown'',''10.0.0'');'
 $sql | & "$pgBin\psql.exe" -h 127.0.0.1 -p 55439 -U catalog -d $database -v ON_ERROR_STOP=1 -q
 if($LASTEXITCODE){throw 'Unknown fixture insert failed'}
 dotnet $cli schema-check
 if($LASTEXITCODE -ne 3){throw 'Unknown schema must be refused'}
 $checks+='unknown schema refused'
 $env:CATALOG_CONNECTION='Host=127.0.0.1;Port=55438;Database=catalog_search;Username=catalog;Timeout=1'
 dotnet $cli schema-check
 if($LASTEXITCODE -ne 1){throw 'Unavailable DB must fail'}
 $checks+='unavailable DB refused'
 $env:CATALOG_TEST_CONNECTION='Host=127.0.0.1;Port=55439;Database=catalog_search_tests;Username=catalog'
 $env:CATALOG_ACCEPTANCE_CONNECTION='Host=127.0.0.1;Port=55439;Database=catalog_search_poc;Username=catalog'
 $env:CATALOG_WORKBOOK=$WorkbookPath
 dotnet test --no-build --logger 'trx;LogFileName=container-regression.trx' --results-directory artifacts/tests
 if($LASTEXITCODE){throw 'Regression failed'}
 @{passed=$checks.Count;checks=$checks;atUtc=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content artifacts/container-db-gate.json
}finally{& "$pgBin\pg_ctl.exe" -D $pgData stop -m fast}
