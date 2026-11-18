[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]+$')][string]$VmHost,
 [Parameter(Mandatory)][string]$IdentityFile,[Parameter(Mandatory)][string]$KnownHostsFile,
 [Parameter(Mandatory)][string]$PackagePath,[Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
 [ValidateSet('Plan','Upload','Status','Deploy')][string]$Action='Plan')
$ErrorActionPreference='Stop'
$sha=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if($sha -cne $ExpectedSha256.ToLowerInvariant()){throw 'Package hash differs from reviewed release.'}
foreach($file in @($IdentityFile,$KnownHostsFile)){if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw 'Verified SSH key and known_hosts files are required.'}}
$sshArgs=@('-i',(Resolve-Path -LiteralPath $IdentityFile).Path,'-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o',('UserKnownHostsFile='+(Resolve-Path -LiteralPath $KnownHostsFile).Path),'-o','ConnectTimeout=10')
$target='catalogdeploy@'+$VmHost
if($Action -eq 'Plan'){[pscustomobject]@{target=$target;packageSha256=$sha;mode='PlanOnly';initialization='Manual on VM';deployment='Web-only; root-approved exact SHA required; DB state mismatch refused';url='SSH tunnel to 127.0.0.1:55440'};return}
if($Action -eq 'Upload'){
 & scp @sshArgs -- (Resolve-Path -LiteralPath $PackagePath).Path "${target}:incoming/$sha.tar"
 if($LASTEXITCODE){throw 'Package upload failed.'}
 Write-Host "Uploaded only. VM administrator must review and run: sudo /usr/local/sbin/catalogctl approve $sha"
}elseif($Action -eq 'Status'){
 & ssh @sshArgs $target 'sudo -n /usr/local/sbin/catalogctl status'
 if($LASTEXITCODE){throw 'Status failed.'}
}else{
 & ssh @sshArgs $target "sudo -n /usr/local/sbin/catalogctl deploy $sha"
 if($LASTEXITCODE){throw 'Deployment rejected or failed. Inspect the reported gate; do not bypass it.'}
}
