[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$NetworkReviewEvidencePath,[Parameter(Mandatory=$true)][string]$ReviewedAncestorAclSha256,[switch]$Apply,[switch]$ApprovedNewScope,[switch]$ApprovedAdditionalBlock)
$ErrorActionPreference='Stop'
# Run on the same reviewed human console. No auto-elevation or account switching.
if(!$Apply){& (Join-Path $PSScriptRoot 'Receive-LipsR4.ps1');return}
if($env:COMPUTERNAME -ine 'DELL3420'){throw 'Dell only'}
$hashManifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'control-hashes.json') -Raw -Encoding UTF8|ConvertFrom-Json
$expectedNames=@('ReceiverBase.ps1','BulkTransfer.cs','candidate-manifest.json','Receive-LipsR4.ps1','Review-LipsR4Recovery.ps1')
if(@($hashManifest.files).Count -ne $expectedNames.Count){throw 'Control inventory differs'}
foreach($name in $expectedNames){$entries=@($hashManifest.files|Where-Object name -ceq $name);if($entries.Count -ne 1){throw 'Control inventory missing or duplicate'};$file=Get-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Force;if($file.Attributes -band [IO.FileAttributes]::ReparsePoint -or $file.Length -ne $entries[0].length -or (Get-FileHash -LiteralPath $file.FullName).Hash -cne $entries[0].sha256){throw ('Control source differs: '+$name)}}
Write-Host 'PREPARED: no rules/listener. Wait for parent sender readiness.'
function Get-ReceiveSyncDecision([string]$Answer) {
 if($Answer -ceq ''){return 'Start'}
 if($Answer -ieq 'CANCEL'){return 'Cancel'}
 throw 'Only empty Enter starts receiving. CANCEL exits. Other input rejected before listener/rules.'
}
$choice=Get-ReceiveSyncDecision ([string](Read-Host 'Empty Enter starts only when parent sender is ready; CANCEL exits'))
if($choice -eq 'Cancel'){
 [pscustomobject]@{status='cancelledBeforeReceive';listenerStarted=$false;rulesCreated=$false;databaseApplied=$false}|ConvertTo-Json
 return
}
$resultPath=Join-Path ([Environment]::GetFolderPath('UserProfile')) ('Downloads\lips-r4-result-'+[Guid]::NewGuid().ToString('N')+'.json')
$outcome=$null
try{
 $output=& (Join-Path $PSScriptRoot 'Receive-LipsR4.ps1') -Apply -ApprovedNewScope:$ApprovedNewScope -ApprovedAdditionalBlock:$ApprovedAdditionalBlock -ReviewedAncestorAclSha256 $ReviewedAncestorAclSha256 -NetworkReviewEvidencePath $NetworkReviewEvidencePath
 $outcome=[ordered]@{status='completed';at=[DateTimeOffset]::UtcNow.ToString('o');receipt=(($output -join "`n")|ConvertFrom-Json);note='Transfer only; DB not applied'}
}catch{$outcome=[ordered]@{status='failedNeedsInspection';at=[DateTimeOffset]::UtcNow.ToString('o');error=$_.Exception.Message;note='Do not rerun blindly. Inspect recovery/cleanup files and received positions first.'}}
$bytes=(New-Object Text.UTF8Encoding($true)).GetPreamble()+[Text.Encoding]::UTF8.GetBytes(($outcome|ConvertTo-Json -Depth 15))
$stream=[IO.File]::Open($resultPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
$read=Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8|ConvertFrom-Json
if($read.status -cne $outcome.status){throw 'Result readback failed'}
[pscustomobject]@{status=$read.status;error=$(if($read.PSObject.Properties['error']){$read.error}else{$null});reportPath=$resultPath;reportSha256=(Get-FileHash -LiteralPath $resultPath).Hash;databaseApplied=$false}|ConvertTo-Json
if($read.status -ne 'completed'){exit 2}

