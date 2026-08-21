$ErrorActionPreference = 'Stop'
$driverInput = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\drivers\bodywalkvr_virtual\resources\input'
$profilePath = Join-Path $driverInput 'bodywalkvr_profile.json'
$bindingName = 'steam.app.546560_bodywalkvr_virtual.json'
$bindingSource = 'C:\NiirMotion\artifacts\steam.app.546560_bodywalkvr_virtual.json'
$backupDirectory = 'C:\NiirMotion\artifacts\bodywalk-driver-backup'

New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
Copy-Item -LiteralPath $profilePath -Destination (Join-Path $backupDirectory 'bodywalkvr_profile.json') -Force
Copy-Item -LiteralPath $bindingSource -Destination (Join-Path $driverInput $bindingName) -Force

$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json
$existing = @($profile.default_bindings | Where-Object { $_.app_key -ne 'steam.app.546560' })
$alyx = [pscustomobject]@{
    app_key = 'steam.app.546560'
    binding_url = $bindingName
}
$profile.default_bindings = @($existing) + $alyx
$profile | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $profilePath -Encoding utf8
