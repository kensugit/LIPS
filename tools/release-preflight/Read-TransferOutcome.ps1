[CmdletBinding()]
param([Parameter(Mandatory)][string]$ResultPath,[Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
$result=Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8|ConvertFrom-Json
$manifest=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$root=[IO.Path]::GetFullPath($manifest.destination)
$files=@(foreach($f in $manifest.files){
 if([IO.Path]::GetFileName($f.name) -cne $f.name -or $f.name -match '[:/\\]'){throw 'Invalid fixed file name'}
 foreach($suffix in @('','.upload')){
  $path=Join-Path $root ($f.name+$suffix)
  $row=[ordered]@{name=$f.name+$suffix;kind=$f.kind;exists=$false;bytes=$null;sha256=$null;expectedBytes=$f.length;expectedSha256=$f.sha256;matchesCompletedFile=$false;readError=$null}
  try{
   if(Test-Path -LiteralPath $path){
    $file=Get-Item -LiteralPath $path -Force
    if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point rejected'}
    $row.exists=$true;$row.bytes=$file.Length;$row.sha256=(Get-FileHash -LiteralPath $path).Hash
    $row.matchesCompletedFile=($suffix -ceq '' -and $row.bytes -eq $f.length -and $row.sha256 -ceq $f.sha256)
   }
  }catch{$row.readError=$_.Exception.Message}
  [pscustomobject]$row
 }
})
$cleanup=@(Get-ChildItem -LiteralPath $root -Filter 'cleanup-*.json' -File|ForEach-Object {
 $r=Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8|ConvertFrom-Json
 [pscustomobject]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash;recordedAt=$r.recordedAt;ownershipTag=$r.ownershipTag;cleanupError=$r.cleanupError;portObservedClear=$r.portObservedClear;ruleRemovalVerified=$r.ruleRemovalVerified;listenersAfterCleanup=$r.listenersAfterCleanup}
})
[pscustomobject]@{
 schema='lips-transfer-diagnostic/v1';packageId=$manifest.packageId;status=$result.status
 error=$result.error;resultPath=$ResultPath;resultSha256=(Get-FileHash -LiteralPath $ResultPath).Hash
 files=$files;cleanupRecords=$cleanup
 allCompletedFilesMatch=(@($files|Where-Object matchesCompletedFile).Count -eq @($manifest.files).Count)
 currentListenerAndRules='Not queried; cleanup records are historical evidence only'
 linuxStaging='Not checked';automaticRetry=$false;databaseChanged=$false
}|ConvertTo-Json -Depth 10
