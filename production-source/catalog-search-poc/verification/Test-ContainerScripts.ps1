$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$errors=@()
Get-ChildItem (Join-Path $root 'deployment') -Filter '*.ps1' | ForEach-Object {
 $tokens=$null;$parseErrors=$null
 [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$parseErrors)
 $errors+=@($parseErrors)
}
if($errors.Count){throw ($errors|Out-String)}
$plan=& (Join-Path $root 'deployment/New-DellCatalogVm.ps1')
if($plan.mode -ne 'PlanOnly'){throw 'VM default must not mutate'}
$testDir=Join-Path $root ('artifacts/container-plan-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$package=Join-Path $testDir 'package.tar';$key=Join-Path $testDir 'fixture.key';$known=Join-Path $testDir 'known_hosts'
Set-Content -LiteralPath $package -Value 'not a deployable archive'
Set-Content -LiteralPath $key -Value 'fixture, not a real key'
Set-Content -LiteralPath $known -Value 'fixture, not a host key'
$sha=(Get-FileHash $package).Hash
$argsForPlan=@{VmHost='fixture.invalid';IdentityFile=$key;KnownHostsFile=$known;PackagePath=$package;ExpectedSha256=$sha}
$plan=& (Join-Path $root 'deployment/Deploy-CatalogFromCodex.ps1') @argsForPlan
if($plan.mode -ne 'PlanOnly'){throw 'Deploy default must not connect'}
$argsForPlan.ExpectedSha256='a'*64
$rejected=$false
try {& (Join-Path $root 'deployment/Deploy-CatalogFromCodex.ps1') @argsForPlan}catch{$rejected=$_.Exception.Message -like '*hash differs*'}
if(-not $rejected){throw 'Mismatched package must be rejected before network access'}
@{passed=4;checks=@('PowerShell parse','VM default plan','deploy default plan without network','package mismatch denied')}|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/container-script-checks.json')
'PowerShell contract checks passed: 4'
