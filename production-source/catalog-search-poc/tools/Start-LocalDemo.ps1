param([Parameter(Mandatory)][string]$PgBin,[Parameter(Mandatory)][string]$PgData,[switch]$NoBrowser,[switch]$Disabled,[string]$Database="catalog_search_poc",[string]$Inbox="")
$ErrorActionPreference = 'Stop'
$catalogRoot = Split-Path $PSScriptRoot -Parent
$statePath = Join-Path $catalogRoot 'artifacts/local-demo.json'
New-Item -ItemType Directory -Path (Split-Path $statePath) -Force | Out-Null
if (Test-Path $statePath) { throw 'Previous demo state exists. Run Stop-LocalDemo.ps1 first.' }
$pgStarted = $false
$web = $null
try {
    if (Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 55439 is already occupied. Stop the previous demo first.' }
    if (Get-NetTCPConnection -LocalPort 55440 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 55440 is already occupied.' }
    & (Join-Path $PgBin 'pg_ctl.exe') -D $PgData -l (Join-Path $catalogRoot 'artifacts/postgres-demo.log') -o '-h 127.0.0.1 -p 55439' start
    if ($LASTEXITCODE) { throw 'PostgreSQL startup failed.' }
    $pgStarted = $true
    if ($Database -notmatch '^catalog_[a-z0-9_]+$') { throw 'Use a catalog development database name.' }
    $env:ConnectionStrings__CatalogSearch = "Host=127.0.0.1;Port=55439;Database=$Database;Username=catalog;Timeout=3;Command Timeout=10"
    if (-not $Inbox) { $Inbox = Join-Path $catalogRoot 'data/inbox' }
    $env:CatalogDemo__Inbox = $Inbox
    $env:CatalogDemo__WatchEnabled = 'true'
    $env:CatalogDemo__Enabled = if ($Disabled) { 'false' } else { 'true' }
    $dll = Join-Path $catalogRoot 'src/CatalogSearch.Web/bin/Debug/net10.0/CatalogSearch.Web.dll'
    if (-not (Test-Path $dll)) { throw 'Build the catalog solution first.' }
    $web = Start-Process dotnet -ArgumentList @(('"'+$dll+'"')) -WorkingDirectory (Join-Path $catalogRoot 'src/CatalogSearch.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $catalogRoot 'artifacts/demo-web.log') -RedirectStandardError (Join-Path $catalogRoot 'artifacts/demo-web.err.log')
    $ready = $false
    for ($attempt=0;$attempt -lt 30;$attempt++) {
        if ($web.HasExited) { throw 'Web process exited. See artifacts/demo-web.err.log.' }
        try { $null=Invoke-RestMethod 'http://127.0.0.1:55440/health';$ready=$true;break } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { throw 'Web startup timed out.' }
    @{ WebPid=$web.Id; WebStarted=$web.StartTime.ToUniversalTime().ToString('o'); PgBin=(Resolve-Path $PgBin).Path; PgData=(Resolve-Path $PgData).Path } | ConvertTo-Json | Set-Content $statePath -Encoding utf8
    if (-not $NoBrowser) { Start-Process 'http://127.0.0.1:55440' }
    Write-Host 'Ready: http://127.0.0.1:55440'
} catch {
    if ($web -and -not $web.HasExited) { Stop-Process -Id $web.Id }
    if ($pgStarted) { & (Join-Path $PgBin 'pg_ctl.exe') -D $PgData stop -m fast | Out-Null }
    throw
}
