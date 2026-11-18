[CmdletBinding()]param([Parameter(Mandatory)][string]$SourceDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$manifestPath=Join-Path $PSScriptRoot 'approved-transfer.json'
if((Get-FileHash -LiteralPath $manifestPath).Hash -cne 'MANIFEST_HASH'){throw 'Transfer manifest changed.'}
$engine=Join-Path $PSScriptRoot 'BulkTransfer.cs'
if((Get-FileHash -LiteralPath $engine).Hash -cne '6FC34FEF08282788FAB8A0941A92BA5C90886FB44609C952331A0A82715D0F65'){throw 'Engine changed.'}
if(!('IwsetBulkTransfer' -as [type])){Add-Type -Path $engine}
$m=Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath|ConvertFrom-Json
$files=@(foreach($f in $m.files){
 $path=Join-Path $SourceDirectory $f.name
 if((Get-Item -LiteralPath $path).Length -ne $f.length -or [IwsetBulkTransfer]::Hash($path) -cne $f.sha256){throw 'Source size/hash mismatch.'}
 $s=New-Object IwsetBulkTransfer+FileSpec;$s.Path=$path;$s.Kind=$f.kind;$s.Length=$f.length;$s.Sha256=$f.sha256;$s
})
$cert=Get-Item 'Cert:\CurrentUser\My\140450CE946887A9352B6611A42F75DD155E35C7'
$report=[Action[IwsetBulkTransfer+Progress]]{param($p)
 Write-Host ('{0}: {1:N1}% / {2:N1} MiB/s / ETA {3:N0}s' -f $p.Kind,(100*$p.Bytes/$p.Total),$p.MiBPerSecond,$p.RemainingSeconds)
}
$timer=[Diagnostics.Stopwatch]::StartNew()
$record=[ordered]@{packageId=$m.packageId;status='started';databaseApplied=$false}
try{
 [IwsetBulkTransfer]::Send('192.168.1.5',5987,$cert,'59336CCBE546B459955D93164EDAF19336AEA743',$m.packageId,$files,$report)
 $record.status='serverAcknowledgedSizeAndSha256'
}catch{$record.status='requiresReview';$record.error=$_.Exception.Message;throw}
finally{
 $record.seconds=[math]::Round($timer.Elapsed.TotalSeconds,2)
 $record|ConvertTo-Json|Tee-Object -FilePath (Join-Path $SourceDirectory ('transfer-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json'))
}
