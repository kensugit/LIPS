function Get-LanPowerSettings {
    $power = @{}
    foreach ($setting in @('STANDBYIDLE','HIBERNATEIDLE')) {
        $query = (& powercfg.exe /query SCHEME_CURRENT SUB_SLEEP $setting) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "Power policy query failed: $setting" }
        $indices = [regex]::Matches($query,'0x([0-9a-fA-F]{8})')
        if ($indices.Count -lt 2) { throw "Could not identify current AC power policy: $setting" }
        # Last two indices are current AC/DC; earlier hex values describe min/max/step.
        $power[$setting] = [Convert]::ToUInt32($indices[$indices.Count-2].Groups[1].Value,16)
    }
    return $power
}
