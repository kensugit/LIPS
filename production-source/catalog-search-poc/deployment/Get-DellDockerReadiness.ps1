[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference='Stop'
$os=Get-CimInstance Win32_OperatingSystem
$system=Get-CimInstance Win32_ComputerSystem
$cpus=@(Get-CimInstance Win32_Processor)
$drive=Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"
$hyperv='Unknown (administrator required)'
try {$hyperv=(Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -ErrorAction Stop).State.ToString()} catch {}
$reasons=@()
if($os.ProductType -ne 1 -or $os.Caption -notmatch 'Windows 11 (Pro|Enterprise|Education)'){$reasons+='Reviewed target is Windows 11 Pro/Enterprise/Education.'}
if($system.TotalPhysicalMemory -lt 16GB){$reasons+='Less than 16GB RAM: review existing iWSET load before allocating a 4GB VM.'}
if($drive.FreeSpace -lt 80GB){$reasons+='Less than 80GB free on C:.'}
if(-not $system.HypervisorPresent -and @($cpus|Where-Object {-not $_.VirtualizationFirmwareEnabled -or -not $_.SecondLevelAddressTranslationExtensions}).Count){$reasons+='BIOS virtualization/SLAT not confirmed.'}
if($hyperv -ne 'Enabled'){$reasons+='Hyper-V is not enabled or could not be inspected.'}
$result=[ordered]@{atUtc=[DateTime]::UtcNow.ToString('o');computer=$env:COMPUTERNAME;os=$os.Caption;build=$os.BuildNumber;ramGiB=[math]::Round($system.TotalPhysicalMemory/1GB,1);freeRamGiB=[math]::Round($os.FreePhysicalMemory/1MB,1);freeDiskGiB=[math]::Round($drive.FreeSpace/1GB,1);hypervisorPresent=$system.HypervisorPresent;hyperV=$hyperv;cpu=@($cpus|Select-Object Name,NumberOfCores,VirtualizationFirmwareEnabled,SecondLevelAddressTranslationExtensions);readyForVmCreation=($reasons.Count -eq 0);reasons=$reasons;existingSwitches=@();dockerCommandPresent=[bool](Get-Command docker -ErrorAction SilentlyContinue)}
if(Get-Command Get-VMSwitch -ErrorAction SilentlyContinue){try{$result.existingSwitches=@(Get-VMSwitch|Select-Object Name,SwitchType)}catch{}}
$json=$result|ConvertTo-Json -Depth 6
if($OutputPath){$json|Set-Content -LiteralPath $OutputPath -Encoding UTF8}
$json
