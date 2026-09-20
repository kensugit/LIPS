param(
    [string]$CatalogRoot = 'C:/Users/kensu/OpenAI/Codex/iWSET/iWSET/商品情報DB/product-catalog-poc-20260917/catalog-search-poc',
    [string]$PgBin = 'C:/Users/kensu/Documents/Codex/2026-09-17/db-chatgpt-conversation-6aa9d142-1c38-83e8/work/pgsql/bin',
    [string]$PgData = 'C:/Users/kensu/Documents/Codex/2026-09-17/db-chatgpt-conversation-6aa9d142-1c38-83e8/work/pgdata-user',
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
$lipsRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $lipsRoot 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
if (Get-NetTCPConnection -LocalPort 55440 -State Listen -ErrorAction SilentlyContinue) {
    $health = Invoke-RestMethod 'http://127.0.0.1:55440/health'
    if ($health.status -ne 'ok') { throw 'Port 55440 is occupied by another service.' }
    Write-Output 'Existing catalog service: http://127.0.0.1:55440'
    if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:55440' }
    return
}
if (-not (Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue)) {
    & (Join-Path $PgBin 'pg_ctl.exe') -D $PgData -l (Join-Path $artifacts 'postgres.log') -o '-h 127.0.0.1 -p 55439' start
    if ($LASTEXITCODE) { throw 'PostgreSQL could not start.' }
}
& (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p 55439 -U catalog -d catalog_search_poc -X -t -A -c 'SELECT 1' | Out-Null
if ($LASTEXITCODE) { throw 'The local catalog database is not available.' }
$settings = @{
    ConnectionStrings__CatalogSearch = 'Host=127.0.0.1;Port=55439;Database=catalog_search_poc;Username=catalog;Timeout=3;Command Timeout=10'
    CatalogDemo__Enabled = 'true'
    CatalogDemo__WatchEnabled = 'false'
}
$previous = @{}
foreach ($key in $settings.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process') }
try {
    $webDll = Join-Path $CatalogRoot 'src/CatalogSearch.Web/bin/Debug/net10.0/CatalogSearch.Web.dll'
    if (-not (Test-Path -LiteralPath $webDll)) { throw 'Build the catalog Web runtime first.' }
    $web = Start-Process dotnet -ArgumentList @('"' + $webDll + '"') -WorkingDirectory (Join-Path $CatalogRoot 'src/CatalogSearch.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifacts 'catalog-web.log') -RedirectStandardError (Join-Path $artifacts 'catalog-web-error.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 40; $attempt++) {
        if ($web.HasExited) { throw 'Catalog Web exited. Check artifacts/catalog-web-error.log.' }
        try { $null = Invoke-RestMethod 'http://127.0.0.1:55440/health'; $ready = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { Stop-Process -Id $web.Id; throw 'Catalog startup timed out.' }
    @{ WebPid=$web.Id; WebStarted=$web.StartTime.ToUniversalTime().ToString('o'); Url='http://127.0.0.1:55440'; Database='catalog_search_poc' } | ConvertTo-Json | Set-Content (Join-Path $artifacts 'catalog-service.json') -Encoding utf8
    Write-Output 'Ready: http://127.0.0.1:55440'
    if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:55440' }
} finally {
    foreach ($key in $settings.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
