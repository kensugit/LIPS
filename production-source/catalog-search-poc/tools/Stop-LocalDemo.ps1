$ErrorActionPreference = 'Stop'
$catalogRoot = Split-Path $PSScriptRoot -Parent
$statePath = Join-Path $catalogRoot 'artifacts/local-demo.json'
if (-not (Test-Path $statePath)) { Write-Host 'No demo started by this launcher.'; exit 0 }
$state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json
$web = Get-Process -Id $state.WebPid -ErrorAction SilentlyContinue
if ($web) {
    $expectedStart = ([DateTime]$state.WebStarted).ToUniversalTime()
    if ([Math]::Abs(($web.StartTime.ToUniversalTime()-$expectedStart).TotalSeconds) -gt 1 -or $web.ProcessName -ne 'dotnet') { throw 'Process identity changed; no process was stopped.' }
    Stop-Process -Id $web.Id
    $web.WaitForExit()
}
& (Join-Path $state.PgBin 'pg_ctl.exe') -D $state.PgData stop -m fast
if ($LASTEXITCODE) { throw 'Could not stop demo PostgreSQL; inspect its state.' }
Remove-Item -LiteralPath $statePath
Write-Host 'Demo stopped.'
