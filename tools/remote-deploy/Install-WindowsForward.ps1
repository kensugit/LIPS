[CmdletBinding()]
param([string]$ClientAddress='10.10.10.1', [switch]$Apply)
$ErrorActionPreference='Stop'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run as administrator.' }
if ($env:COMPUTERNAME -ine 'Dell3420') { throw 'Dell3420 only.' }
$ip=[Net.IPAddress]::Parse($ClientAddress)
if ($ip.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $ClientAddress -notmatch '^(10\.10\.10|192\.168\.1)\.\d{1,3}$') { throw 'Provide the exact Codex client IPv4 on the VPN or store LAN.' }
if (-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq '192.168.1.5')) { throw 'Expected Dell IP is absent.' }
if (Get-NetTCPConnection -State Listen -LocalPort 22222 -ErrorAction SilentlyContinue) { throw 'External port 22222 is occupied.' }
if (Get-NetFirewallRule -Name 'LIPS-Codex-SSH' -ErrorAction SilentlyContinue) { throw 'Deployment firewall rule already exists.' }
Write-Output "Plan: $ClientAddress -> 192.168.1.5:22222 -> 127.0.0.1:22223; public-key-only restricted SSH."
if (-not $Apply) { return }
$connection=New-Object Net.Sockets.TcpClient
try {
    $pending=$connection.ConnectAsync('127.0.0.1',22223)
    if (-not $pending.Wait(5000) -or -not $connection.Connected) { throw 'WSL dedicated SSH is not reachable through Windows localhost.' }
} finally { $connection.Dispose() }
$forwardKey='HKLM:\SYSTEM\CurrentControlSet\Services\PortProxy\v4tov4\tcp'
if ((Get-ItemProperty -LiteralPath $forwardKey -ErrorAction SilentlyContinue).PSObject.Properties.Name -contains '192.168.1.5/22222') { throw 'Existing portproxy entry found; inspect it first.' }
$helper=Get-Service iphlpsvc
if ($helper.StartType -eq 'Disabled') { throw 'IP Helper is disabled. Inspect host policy before enabling forwarding.' }
Start-Service iphlpsvc
& netsh.exe interface portproxy add v4tov4 listenaddress=192.168.1.5 listenport=22222 connectaddress=127.0.0.1 connectport=22223 protocol=tcp
if ($LASTEXITCODE -ne 0) { throw 'Could not create dedicated port forwarding.' }
try {
    New-NetFirewallRule -Name 'LIPS-Codex-SSH' -DisplayName 'LIPS Codex restricted SSH' -Direction Inbound -Action Allow -Profile Any -Protocol TCP -LocalAddress 192.168.1.5 -LocalPort 22222 -RemoteAddress $ClientAddress | Out-Null
} catch {
    & netsh.exe interface portproxy delete v4tov4 listenaddress=192.168.1.5 listenport=22222
    throw
}
Write-Output 'Dedicated forwarding installed. Existing iWSET, gateway and database ports unchanged.'
& netsh.exe interface portproxy show v4tov4
