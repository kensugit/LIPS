[CmdletBinding()]
param([Parameter(Mandatory)][string]$KitDirectory)
$ErrorActionPreference='Stop'
$kit=(Resolve-Path -LiteralPath $KitDirectory).Path
# Parse, do not execute the receiver or touch certificates/firewall/network.
foreach($file in Get-ChildItem -LiteralPath $kit -Filter '*.ps1' -File){
 $tokens=$null;$errors=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('PowerShell syntax: '+$file.Name+': '+($errors.Message -join ', '))}
 if($file.Name -eq 'Activate-Receive.ps1'){
  $calls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst]},$true))
  foreach($call in $calls){
   if($call.GetCommandName() -in @('Initialize-CandidateEngine','Add-Type','Assert-ReviewedResume') -or
      ($call.InvocationOperator -eq 'Dot' -and $call.Extent.Text -match 'Receive-LipsR4|ReceiverBase')){
    throw 'Activation must delegate engine initialization/resume checks to receive preconditions; duplicate engine load risk'
   }
  }
 }
}
[pscustomobject]@{status='ParsedAndLifecycleChecked';powershell=$PSVersionTable.PSVersion.ToString();receiverExecuted=$false;networkChanges=$false}|ConvertTo-Json
