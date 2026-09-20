$ErrorActionPreference = 'Stop'
$installRoot = 'C:\ProgramData\LIPS-LAN'
if (-not (Test-Path -LiteralPath "$installRoot\previous-wsl-task.xml")) { throw 'Original task backup is missing.' }
$previous = Get-Content "$installRoot\previous-settings.json" -Raw | ConvertFrom-Json
if (Get-ScheduledTask -TaskName 'LIPS-LAN-Gateway' -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName 'LIPS-LAN-Gateway'
    Unregister-ScheduledTask -TaskName 'LIPS-LAN-Gateway' -Confirm:$false
}
Get-NetFirewallRule -Name 'LIPS-LAN-55441' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Stop-ScheduledTask -TaskName 'LIPS-Start-WSL' -ErrorAction SilentlyContinue
Register-ScheduledTask -TaskName 'LIPS-Start-WSL' -Xml (Get-Content "$installRoot\previous-wsl-task.xml" -Raw) -Force | Out-Null
if ($previous.WslWasRunning) { Start-ScheduledTask -TaskName 'LIPS-Start-WSL' }
foreach ($setting in @('STANDBYID','HIBERNATEID')) {
    & powercfg.exe /setacvalueindex SCHEME_CURRENT SUB_SLEEP $setting ([string]$previous.Power.$setting)
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore AC power policy.' }
}
& powercfg.exe /setactive SCHEME_CURRENT
Write-Output 'Previous task and AC power policy restored; gateway task/firewall removed. Installation files retained for investigation. Database unchanged.'
