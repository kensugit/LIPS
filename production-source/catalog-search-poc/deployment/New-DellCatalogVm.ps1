[CmdletBinding(SupportsShouldProcess)]
param([string]$IsoPath,[string]$IsoSha256,[string]$SwitchName,[switch]$EnableHyperV,[switch]$Apply)
$ErrorActionPreference='Stop'
if(-not $Apply){[pscustomobject]@{mode='PlanOnly';vm='CatalogLinux';ramGiB=4;cpu=2;diskGiB=64;folder='C:\CatalogVM';network='Use an existing reviewed switch; never modify the iWSET NIC';hyperV=$EnableHyperV.IsPresent;restart='Never automatic';next='Run Get-DellDockerReadiness.ps1 on Dell as administrator'};return}
if($env:COMPUTERNAME -ine 'DELL3420'){throw 'This script is restricted to the reviewed Dell host.'}
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(-not $admin){throw 'Run as administrator on Dell.'}
if($EnableHyperV){
 if($PSCmdlet.ShouldProcess('DELL3420','Enable Hyper-V without restarting')){Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All -NoRestart;Write-Host 'Schedule a manual restart after checking the running iWSET workload. Then rerun readiness.'};return
}
$readiness=(& "$PSScriptRoot/Get-DellDockerReadiness.ps1"|Out-String|ConvertFrom-Json)
if(-not $readiness.readyForVmCreation -or $readiness.freeRamGiB -lt 6){throw 'Readiness failed or less than 6GB free RAM. Review report.'}
if(-not $IsoPath -or $IsoSha256 -notmatch '^[a-fA-F0-9]{64}$' -or -not $SwitchName){throw 'Verified Ubuntu 24.04 amd64 ISO path/SHA256 and existing switch are required.'}
if((Get-FileHash -LiteralPath $IsoPath -Algorithm SHA256).Hash -ine $IsoSha256){throw 'ISO hash mismatch.'}
$network=Get-VMSwitch -Name $SwitchName
if($network.SwitchType -ne 'External'){throw 'Use an existing reviewed external switch with a reachable reserved guest IP. Do not alter the iWSET NIC automatically.'}
if((Get-VM -Name CatalogLinux -ErrorAction SilentlyContinue) -or (Test-Path -LiteralPath 'C:\CatalogVM')){throw 'VM or directory already exists; inspect rather than overwrite.'}
if($PSCmdlet.ShouldProcess('C:\CatalogVM','Create CatalogLinux VM; no start or reboot')){
 New-Item -ItemType Directory -Path 'C:\CatalogVM' | Out-Null
 New-VM -Name CatalogLinux -Generation 2 -MemoryStartupBytes 4GB -NewVHDPath 'C:\CatalogVM\CatalogLinux.vhdx' -NewVHDSizeBytes 64GB -SwitchName $SwitchName -Path 'C:\CatalogVM' | Out-Null
 Set-VMProcessor -VMName CatalogLinux -Count 2
 Set-VM -Name CatalogLinux -AutomaticStartAction Start -AutomaticStartDelay 60 -AutomaticStopAction ShutDown
 Set-VMFirmware -VMName CatalogLinux -EnableSecureBoot On -SecureBootTemplate MicrosoftUEFICertificateAuthority
 Add-VMDvdDrive -VMName CatalogLinux -Path (Resolve-Path -LiteralPath $IsoPath).Path
 Set-VMFirmware -VMName CatalogLinux -FirstBootDevice (Get-VMDvdDrive -VMName CatalogLinux)
 Write-Host 'VM created but stopped. Install Ubuntu 24.04 LTS amd64 + OpenSSH, assign a reserved address, then run bootstrap-ubuntu.sh.'
}
