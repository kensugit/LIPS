$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../tools/Get-LanPowerSettings.ps1')
# Actual read-only Windows calls catch invalid aliases before shipping.
$live = Get-LanPowerSettings
if (-not $live.ContainsKey('STANDBYIDLE') -or -not $live.ContainsKey('HIBERNATEIDLE')) { throw 'Live power query did not return both settings' }
# Exercise positional parsing without changing the machine power policy.
function powercfg.exe {
    $global:LASTEXITCODE = 0
    'Min: 0x00000000'; 'Max: 0xffffffff'; 'Step: 0x00000001'
    'AC: 0x00000708'; 'DC: 0x00000384'
}
$fixture = Get-LanPowerSettings
if ($fixture.STANDBYIDLE -ne 1800 -or $fixture.HIBERNATEIDLE -ne 1800) { throw 'AC/DC parsing regression' }
Remove-Item Function:powercfg.exe
'Power readback and AC/DC fixture checks passed; no settings changed.'
