[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskIdentity=[Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$taskIdentity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run in administrator PowerShell as the owner of Ubuntu-24.04.' }
if ($env:COMPUTERNAME -ine 'Dell3420') { throw 'Dell3420 only.' }
if (-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq '192.168.1.5')) { throw 'Expected Dell address is absent.' }
foreach ($taskLine in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'SHA256SUMS'))) {
    if ($taskLine -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$') { throw 'Invalid package hash list.' }
    $taskExpected=$Matches[1]; $taskFile=Join-Path $PSScriptRoot $Matches[2]
    if ((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) { throw "Hash mismatch: $taskFile" }
}
$taskOwner=Get-ScheduledTask -TaskName 'LIPS-Start-WSL'
$taskOwnerSid=(New-Object Security.Principal.NTAccount($taskOwner.Principal.UserId)).Translate([Security.Principal.SecurityIdentifier]).Value
if ($taskOwnerSid -ne $taskIdentity.User.Value) { throw 'Run as the Windows account owning LIPS-Start-WSL / Ubuntu-24.04.' }
$taskWslPath=(& wsl.exe -d Ubuntu-24.04 --exec wslpath -a $PSScriptRoot).Trim()
if ($LASTEXITCODE -ne 0 -or -not $taskWslPath.StartsWith('/mnt/')) { throw 'Cannot resolve package in Ubuntu.' }
& wsl.exe -d Ubuntu-24.04 -u root --exec bash "$taskWslPath/upgrade-ubuntu.sh" --apply
if ($LASTEXITCODE -ne 0) { throw 'Ubuntu installation failed. Windows firewall not changed.' }
$taskClient=[Net.Sockets.TcpClient]::new()
try { if (-not $taskClient.ConnectAsync('127.0.0.1',22223).Wait(5000) -or -not $taskClient.Connected) { throw 'WSL SSH backend is unavailable.' } } finally { $taskClient.Dispose() }
$taskBackup=Join-Path 'C:\ProgramData\LIPS-Codex' ('setup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $taskBackup | Out-Null
$taskForwardKey='HKLM:\SYSTEM\CurrentControlSet\Services\PortProxy\v4tov4\tcp'
$taskForward=(Get-ItemProperty -LiteralPath $taskForwardKey -ErrorAction SilentlyContinue).'192.168.1.5/22222'
if ($taskForward -and $taskForward -ne '127.0.0.1/22223') { throw 'Unexpected existing forward; stopped.' }
$taskRule=Get-NetFirewallRule -Name 'LIPS-Codex-SSH' -ErrorAction SilentlyContinue
if ($taskRule) {
    $taskRule | Get-NetFirewallAddressFilter | Export-Clixml (Join-Path $taskBackup 'firewall-addresses.xml')
    $taskPort=$taskRule | Get-NetFirewallPortFilter
    if ($taskRule.Direction -ne 'Inbound' -or $taskRule.Action -ne 'Allow' -or $taskPort.Protocol -ne 'TCP' -or "$($taskPort.LocalPort)" -ne '22222') { throw 'Unexpected existing firewall rule; stopped.' }
}
if ((Get-Service iphlpsvc).StartType -eq 'Disabled') { throw 'IP Helper disabled; inspect host policy.' }
Start-Service iphlpsvc
if (-not $taskForward) {
    & netsh.exe interface portproxy add v4tov4 listenaddress=192.168.1.5 listenport=22222 connectaddress=127.0.0.1 connectport=22223 protocol=tcp
    if ($LASTEXITCODE -ne 0) { throw 'Port forwarding failed.' }
}
# Exact development-PC addresses only; do not open the LAN/VPN subnet.
if ($taskRule) { Set-NetFirewallRule -Name 'LIPS-Codex-SSH' -Enabled True -RemoteAddress @('10.10.10.1','192.168.1.110') }
else { New-NetFirewallRule -Name 'LIPS-Codex-SSH' -DisplayName 'LIPS Codex restricted SSH' -Direction Inbound -Action Allow -Profile Any -Protocol TCP -LocalAddress 192.168.1.5 -LocalPort 22222 -RemoteAddress @('10.10.10.1','192.168.1.110') | Out-Null }
Write-Output 'LIPS deployment environment installed. Return this output to Codex; no application deployment performed yet.'
Get-NetFirewallRule -Name 'LIPS-Codex-SSH' | Get-NetFirewallAddressFilter | Select-Object LocalAddress,RemoteAddress
