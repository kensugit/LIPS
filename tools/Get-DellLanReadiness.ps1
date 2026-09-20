# Read-only diagnostics. Run as the Windows user who owns Ubuntu-24.04.
# The WSL queries may start an existing stopped distribution.
$ErrorActionPreference = 'Stop'
function Section([string]$Name) { Write-Output "`n=== $Name ===" }
Section 'Windows'
whoami
Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber | Format-List
Section 'IPv4'
Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '169.254.*' } |
    Select-Object InterfaceAlias, IPAddress, PrefixLength, PrefixOrigin | Format-Table -AutoSize
Get-NetConnectionProfile | Select-Object InterfaceAlias, NetworkCategory | Format-Table -AutoSize
Section 'WSL version and distributions'
wsl --version
wsl --list --verbose
Section 'Selected WSL settings'
$config = Join-Path $env:USERPROFILE '.wslconfig'
if (Test-Path -LiteralPath $config) {
    Get-Content -LiteralPath $config | Where-Object { $_ -match '^\s*(\[|networkingMode\s*=|localhostForwarding\s*=|vmIdleTimeout\s*=|memory\s*=|processors\s*=)' }
}
Section 'LIPS startup task'
$task = Get-ScheduledTask -TaskName 'LIPS-Start-WSL' -ErrorAction SilentlyContinue
if ($task) {
    $task | Select-Object TaskName, State | Format-List
    $task.Principal | Select-Object UserId, LogonType, RunLevel | Format-List
    $task.Settings | Select-Object ExecutionTimeLimit, DisallowStartIfOnBatteries, StopIfGoingOnBatteries, StartWhenAvailable, RestartCount, RestartInterval | Format-List
    $task.Triggers | Select-Object Enabled, StartBoundary, UserId, Delay | Format-List
    $task.Actions | Where-Object { $_.Execute -match '(^|[\\/])wsl(\.exe)?$' } | Select-Object Execute, Arguments | Format-List
    Get-ScheduledTaskInfo -TaskName 'LIPS-Start-WSL' | Select-Object LastRunTime, LastTaskResult | Format-List
} else { Write-Output 'LIPS-Start-WSL task missing' }
Section 'Relevant Windows listeners'
Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 80,443,8443,55439,55440 } |
    Select-Object LocalAddress, LocalPort, OwningProcess | Format-Table -AutoSize
Section 'Existing port forwarding'
netsh interface portproxy show all
Section 'Ubuntu'
wsl -d Ubuntu-24.04 --exec bash -lc 'id; hostname -I; systemctl is-enabled docker; systemctl is-active docker; docker ps --format "{{.Names}}: {{.Status}}"'
Section 'Catalog API'
try {
    $options = Invoke-RestMethod 'http://127.0.0.1:55440/api/options' -TimeoutSec 15
    Write-Output "Catalog total: $($options.total)"
} catch { Write-Output 'Catalog API did not respond successfully within 15 seconds.' }
