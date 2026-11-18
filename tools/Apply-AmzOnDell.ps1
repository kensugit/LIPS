[CmdletBinding()]param([switch]$Apply)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($env:COMPUTERNAME -cne 'DELL3420'){throw 'Dell only.'}
$root='C:\ProgramData\LIPS-Codex\Inbox\LIPS-AMZ-20260925'
$zip=Join-Path $root 'LIPS-AMZ-20260925.zip'
if((Get-Item -LiteralPath $zip).Length -ne PACKAGE_LENGTH -or (Get-FileHash -LiteralPath $zip).Hash -cne 'PACKAGE_HASH'){throw 'Received package size/hash mismatch.'}
$stage=Join-Path $root ('apply-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if(Test-Path -LiteralPath $stage){throw 'Stage exists; inspect previous operation.'}
Expand-Archive -LiteralPath $zip -DestinationPath $stage
$script=Join-Path $stage 'apply.sh'
$linuxPath=(& wsl.exe -d Ubuntu-24.04 -u root --exec wslpath -a $script).Trim()
if($LASTEXITCODE -or !$linuxPath.StartsWith('/mnt/c/ProgramData/LIPS-Codex/Inbox/LIPS-AMZ-20260925/')){throw 'WSL path mismatch.'}
$mode=if($Apply){'--apply'}else{'--check'}
$previousPreference=$ErrorActionPreference
try {
 $ErrorActionPreference='Continue'
 & wsl.exe -d Ubuntu-24.04 -u root --exec bash $linuxPath $mode 2>&1|ForEach-Object { $_.ToString() }|Tee-Object -FilePath (Join-Path $root ('result-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log'))
 $nativeExitCode=$LASTEXITCODE
} finally { $ErrorActionPreference=$previousPreference }
if($nativeExitCode){throw 'Dell import failed. Preserve the log and backup; do not restore automatically.'}
Write-Output ('AMZ Dell operation succeeded: '+$mode)
