$ErrorActionPreference = 'Stop'
# Task runs under the Windows account that owns the distribution, including before logon.
while ($true) {
    $process = Start-Process -FilePath "$env:WINDIR\System32\wsl.exe" -ArgumentList '-d Ubuntu-24.04 --exec /bin/sleep infinity' -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    Start-Sleep -Seconds 10
}
