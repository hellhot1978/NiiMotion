$ErrorActionPreference = 'Stop'

$targets = @(
    'C:\Program Files\BodyWalkVR',
    'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\drivers\bodywalkvr_virtual'
)
$vrPathReg = 'C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\vrpathreg.exe'

if (Test-Path -LiteralPath $vrPathReg) {
    & $vrPathReg removedriver $targets[1]
}

foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolved = (Resolve-Path -LiteralPath $target).Path
    if ($resolved -ne $target) { throw "Beklenmeyen BodyWalk hedefi: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
