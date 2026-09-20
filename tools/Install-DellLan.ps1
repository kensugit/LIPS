[CmdletBinding()]
param(
    [string]$WarehouseNetwork = '192.168.100.0/24',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$installRoot = 'C:\ProgramData\LIPS-LAN'
$taskOwner = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$taskOwner).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run in elevated Windows PowerShell as the owner of Ubuntu-24.04.' }
if ($env:COMPUTERNAME -ine 'Dell3420') { throw 'This package targets Dell3420 only.' }
if (-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq '192.168.1.5')) { throw 'Expected Dell IPv4 address 192.168.1.5 is absent.' }
if ($WarehouseNetwork -notmatch '^(192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})/(\d{1,2})$') { throw 'Provide a private IPv4 CIDR for the warehouse.' }
$warehouseIp = [Net.IPAddress]::Parse($WarehouseNetwork.Split('/')[0])
$prefix = [int]$WarehouseNetwork.Split('/')[1]
if ($prefix -lt 16 -or $prefix -gt 30) { throw 'Warehouse prefix must be between /16 and /30.' }
$bytes = $warehouseIp.GetAddressBytes()
for ($bit=$prefix; $bit -lt 32; $bit++) {
    if (($bytes[[int][Math]::Floor($bit/8)] -band (1 -shl (7-($bit%8)))) -ne 0) { throw 'Use the subnet network address, not a host address.' }
}
$networks = @('192.168.1.0/24','10.10.10.0/24',$WarehouseNetwork) | Select-Object -Unique
$oldTask = Get-ScheduledTask -TaskName 'LIPS-Start-WSL'
if ($oldTask.Principal.LogonType -ne 'Interactive') { throw 'Existing WSL task is no longer Interactive; review it before applying this first-install package.' }
$oldSid = (New-Object Security.Principal.NTAccount($oldTask.Principal.UserId)).Translate([Security.Principal.SecurityIdentifier]).Value
if ($oldSid -ne $taskOwner.User.Value) { throw 'Run as the account that owns the existing LIPS-Start-WSL task.' }
if ((Get-NetTCPConnection -State Listen -LocalPort 55441 -ErrorAction SilentlyContinue) -or (Get-ScheduledTask -TaskName 'LIPS-LAN-Gateway' -ErrorAction SilentlyContinue)) { throw 'Port or gateway task already exists. Inspect it before reinstalling.' }
$resumePreparation = $false
if (Test-Path -LiteralPath $installRoot) {
    $existing = @(Get-ChildItem -LiteralPath $installRoot -Force)
    if ($existing.Count -ne 1 -or $existing[0].Name -ne 'previous-wsl-task.xml' -or $existing[0].PSIsContainer) { throw 'Installation directory contains more than a preparation-only backup; inspect before reinstalling.' }
    $savedTask = [IO.File]::ReadAllText("$installRoot\previous-wsl-task.xml").Trim()
    $currentTask = (Export-ScheduledTask -TaskName 'LIPS-Start-WSL').Trim()
    if ($savedTask -ne $currentTask) { throw 'The WSL task changed after the preparation backup; inspect before reinstalling.' }
    if (Get-NetFirewallRule -Name 'LIPS-LAN-55441' -ErrorAction SilentlyContinue) { throw 'A previous gateway firewall rule exists; inspect before reinstalling.' }
    $resumePreparation = $true
    Write-Output 'Verified preparation-only directory; original WSL task is unchanged. Resuming without deleting the backup.'
}
$packageRoot = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $file = [IO.Path]::GetFullPath((Join-Path $packageRoot $entry.path))
    if (-not $file.StartsWith($packageRoot.TrimEnd('\')+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256) { throw "Package hash mismatch: $($entry.path)" }
}
Write-Output "URL: http://192.168.1.5:55441; allowed networks: $($networks -join ', ')"
Write-Output 'Plan: gateway + scoped firewall rule; password-backed startup WSL task; no task time limit; battery allowed; AC sleep/hibernate disabled.'
Write-Output 'Database, existing iWSET services, network category and DC power settings remain unchanged.'
if (-not $Apply) { Write-Output 'Plan only. Re-run with -Apply to install.'; return }
. (Join-Path $packageRoot 'Get-LanPowerSettings.ps1')
$power = Get-LanPowerSettings
# Verify the selected account has this WSL distribution before making changes.
$distros = ((& wsl.exe --list --quiet) -join "`n").Replace([string][char]0,'')
if ($distros -notmatch '(?m)^\s*Ubuntu-24\.04\s*$') { throw 'Ubuntu-24.04 is not registered for this Windows account.' }
$credential = Get-Credential -UserName $taskOwner.Name -Message 'Enter this Windows account password (not PIN). Stored by Windows Task Scheduler for startup before logon; not written to package or logs.'
if (-not $credential) { throw 'Credential entry cancelled.' }
$credentialSid = (New-Object Security.Principal.NTAccount($credential.UserName)).Translate([Security.Principal.SecurityIdentifier]).Value
if ($credentialSid -ne $taskOwner.User.Value) { throw 'Use the same Windows account that owns Ubuntu-24.04.' }
if (-not $resumePreparation) { New-Item -ItemType Directory -Path $installRoot | Out-Null }
& icacls.exe $installRoot /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not protect installation directory.' }
if (-not $resumePreparation) { Export-ScheduledTask -TaskName 'LIPS-Start-WSL' | Set-Content -LiteralPath "$installRoot\previous-wsl-task.xml" -Encoding Unicode }
$oldTaskRunning = $oldTask.State -eq 'Running'
@{Power=$power; WslWasRunning=$oldTaskRunning; Networks=$networks; Owner=$taskOwner.Name} | ConvertTo-Json | Set-Content "$installRoot\previous-settings.json" -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $packageRoot 'gateway') -Destination $installRoot -Recurse
Copy-Item -LiteralPath (Join-Path $packageRoot 'Keep-LipsWsl.ps1') -Destination $installRoot
Copy-Item -LiteralPath (Join-Path $packageRoot 'Remove-DellLan.ps1') -Destination $installRoot
@{ PublicOrigin='http://192.168.1.5:55441'; AllowedNetworks=($networks -join ';'); Upstream='http://127.0.0.1:55440/' } | ConvertTo-Json | Set-Content "$installRoot\gateway\appsettings.json" -Encoding UTF8
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
$trigger = New-ScheduledTaskTrigger -AtStartup
try {
    $wslAction = New-ScheduledTaskAction -Execute "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$installRoot+'\Keep-LipsWsl.ps1"') -WorkingDirectory $installRoot
    $plain = $credential.GetNetworkCredential().Password
    try { Register-ScheduledTask -TaskName 'LIPS-Start-WSL' -Action $wslAction -Trigger $trigger -Settings $settings -User $credential.UserName -Password $plain -RunLevel Highest -Force | Out-Null }
    finally { $plain=$null; $credential=$null }
    if ($oldTaskRunning) { Stop-ScheduledTask -TaskName 'LIPS-Start-WSL' }
    Start-ScheduledTask -TaskName 'LIPS-Start-WSL'
    $gatewayAction = New-ScheduledTaskAction -Execute "$installRoot\gateway\LipsLanGateway.exe" -WorkingDirectory "$installRoot\gateway"
    $gatewayPrincipal = New-ScheduledTaskPrincipal -UserId 'S-1-5-19' -LogonType ServiceAccount -RunLevel Limited
    Register-ScheduledTask -TaskName 'LIPS-LAN-Gateway' -Action $gatewayAction -Trigger $trigger -Settings $settings -Principal $gatewayPrincipal | Out-Null
    New-NetFirewallRule -Name 'LIPS-LAN-55441' -DisplayName 'LIPS LAN VPN Warehouse' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 55441 -LocalAddress 192.168.1.5 -RemoteAddress $networks -Profile Any -Program "$installRoot\gateway\LipsLanGateway.exe" | Out-Null
    foreach ($setting in @('STANDBYIDLE','HIBERNATEIDLE')) {
        & powercfg.exe /setacvalueindex SCHEME_CURRENT SUB_SLEEP $setting 0
        if ($LASTEXITCODE -ne 0) { throw 'Could not update AC power policy.' }
    }
    & powercfg.exe /setactive SCHEME_CURRENT
    if ($LASTEXITCODE -ne 0) { throw 'Could not activate power policy.' }
    Start-ScheduledTask -TaskName 'LIPS-LAN-Gateway'
    $ready = $false
    for ($attempt=0; $attempt -lt 24; $attempt++) {
        Start-Sleep -Seconds 5
        try { $result = Invoke-RestMethod 'http://192.168.1.5:55441/api/options' -TimeoutSec 4; if ($null -ne $result.total) { $ready=$true; break } } catch { }
    }
    if (-not $ready) {
        foreach ($failedTask in @('LIPS-Start-WSL','LIPS-LAN-Gateway')) { Get-ScheduledTaskInfo -TaskName $failedTask | Select-Object TaskName,LastTaskResult | Format-Table }
        throw 'Gateway did not pass HTTP/DB readiness within the startup window.'
    }
    Write-Output "Installed. Catalog total: $($result.total). URL: http://192.168.1.5:55441"
    Get-ScheduledTask -TaskName 'LIPS-Start-WSL','LIPS-LAN-Gateway' | Select-Object TaskName,State
    Write-Output 'Next: verify from store LAN, VPN and warehouse; close Ubuntu, then verify; finally verify after a planned Windows restart without logon.'
} catch {
    Write-Warning 'Installation failed. Restoring the previous task/firewall/power settings.'
    & "$installRoot\Remove-DellLan.ps1"
    throw
}
