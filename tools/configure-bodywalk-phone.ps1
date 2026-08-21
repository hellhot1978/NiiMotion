$ErrorActionPreference = 'Stop'
$installDirectory = 'C:\Program Files\BodyWalkVR'
$settingsPath = Join-Path $installDirectory 'settings.json'
$backupPath = 'C:\NiirMotion\artifacts\bodywalk-settings-before-phone.json'

New-Item -ItemType Directory -Path (Split-Path $backupPath) -Force | Out-Null
Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force

Get-Process BodyWalkVR -ErrorAction SilentlyContinue | Stop-Process -Force
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.tracker_source = 'Phone (owoTrack)'
$settings.step_input_source = 'Phone (owoTrack)'
$settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath -Encoding utf8

Start-Process -FilePath (Join-Path $installDirectory 'BodyWalkVR.exe') -WorkingDirectory $installDirectory
