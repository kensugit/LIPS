[CmdletBinding()]param([switch]$Receive,[switch]$ApproveTemporaryFirewall)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$manifestPath=Join-Path $PSScriptRoot 'approved-transfer.json'
if((Get-FileHash -LiteralPath $manifestPath).Hash -cne 'MANIFEST_HASH'){throw 'Transfer manifest changed.'}
$engine=Join-Path $PSScriptRoot 'BulkTransfer.cs'
if((Get-FileHash -LiteralPath $engine).Hash -cne '6FC34FEF08282788FAB8A0941A92BA5C90886FB44609C952331A0A82715D0F65'){throw 'Transfer engine changed.'}
$m=Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath|ConvertFrom-Json
if(!('IwsetBulkTransfer' -as [type])){Add-Type -Path $engine}
$root='C:\ProgramData\LIPS-Codex\Inbox\LIPS-AMZ-20260925'
[IwsetBulkTransfer]::SafePath($root)
$files=@(foreach($f in $m.files){
 $s=New-Object IwsetBulkTransfer+FileSpec
 $s.Path=Join-Path $root $f.name;$s.Kind=$f.kind;$s.Length=$f.length;$s.Sha256=$f.sha256
 [IwsetBulkTransfer]::SafePath($s.Path);[IwsetBulkTransfer]::SafePath($s.Path+'.upload');$s
})
function Get-Receipt {
 $results=@(foreach($f in $files){
  $exists=Test-Path -LiteralPath $f.Path
  $partial=Test-Path -LiteralPath ($f.Path+'.upload')
  $length=if($exists){(Get-Item -LiteralPath $f.Path).Length}elseif($partial){(Get-Item -LiteralPath ($f.Path+'.upload')).Length}else{0}
  $hash=if($exists){[IwsetBulkTransfer]::Hash($f.Path)}else{$null}
  [pscustomobject]@{path=$f.Path;bytes=$length;sha256=$hash;verified=($exists -and $length -eq $f.Length -and $hash -ceq $f.Sha256)}
 })
 [pscustomobject]@{packageId=$m.packageId;files=$results;databaseApplied=$false}
}
Get-Receipt|ConvertTo-Json -Depth 5
if(!$Receive){return}
if(!$ApproveTemporaryFirewall){throw 'Requires -ApproveTemporaryFirewall for the temporary source-restricted 5987 rule.'}
if($env:COMPUTERNAME -cne 'DELL3420'){throw 'Dell only.'}
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator required.'}
if(@([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()|Where-Object Port -eq 5987).Count){throw 'Port 5987 is busy. Do not stop its owner.'}
if(@(Get-NetFirewallProfile|Where-Object {!$_.Enabled}).Count){throw 'Firewall must be enabled.'}
$rule='LIPS-AMZ-20260925-Temporary-5987'
if(Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue){throw 'Existing temporary rule needs review.'}
$cert=Get-Item 'Cert:\LocalMachine\My\59336CCBE546B459955D93164EDAF19336AEA743'
if(!$cert.HasPrivateKey -or (Get-Date) -lt $cert.NotBefore -or (Get-Date) -gt $cert.NotAfter){throw 'Server certificate unavailable.'}
if(!(Test-Path -LiteralPath $root)){New-Item -ItemType Directory -Path $root -Force|Out-Null}
& icacls.exe $root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if($LASTEXITCODE){throw 'Inbox ACL setup failed.'}
$lock=$null;$created=$false;$timer=[Diagnostics.Stopwatch]::StartNew()
try {
 $lock=[IO.File]::Open((Join-Path $root 'receive.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 New-NetFirewallRule -Name $rule -DisplayName $rule -Direction Inbound -Action Allow -Protocol TCP -LocalAddress 192.168.1.5 -LocalPort 5987 -RemoteAddress 192.168.1.110,10.10.10.0/24 -Profile Any -Enabled True|Out-Null
 $created=$true
 [IwsetBulkTransfer]::Receive('192.168.1.5',5987,'192.168.1.110',$cert,'140450CE946887A9352B6611A42F75DD155E35C7',$m.packageId,$files,120)
 $receipt=Get-Receipt
 if(@($receipt.files|Where-Object {!$_.verified}).Count){throw 'Receiver verification incomplete.'}
 $receipt|Add-Member -NotePropertyName seconds -NotePropertyValue ([math]::Round($timer.Elapsed.TotalSeconds,2))
 $receipt|ConvertTo-Json -Depth 5|Tee-Object -FilePath (Join-Path $root 'receipt.json')
}finally{
 if($created){Remove-NetFirewallRule -Name $rule}
 if($lock){$lock.Dispose()}
}
