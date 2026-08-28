param([switch]$Strict)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Require-File([string]$RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release file is missing: $RelativePath"
    }
}

$requiredLegal = @(
    'LICENSE.md', 'PRIVACY.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md',
    'src/NiiRMotion.App/Assets/Fonts/Inter-LICENSE.txt',
    'third_party/psmoveapi/4.0.12/COPYING',
    'third_party/licenses/WiimoteLib.NetCore-MIT.txt',
    'native/openxr-layer/OPENXR-SDK-LICENSE.txt'
)
$requiredLegal | ForEach-Object { Require-File $_ }

$tracked = @(& git -C $root ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect tracked files.' }
$privatePatterns = @(
    '^data/', '^logs/', '^recordings/', '^artifacts/',
    '^config/personal-', '^config/model-history/',
    '^config/psmove-assignments\.json$', '^config/psmove-calibrations\.json$',
    '\.niirmotion\.backup$', '\.pdb$'
)
$private = $tracked | Where-Object {
    $candidate = $_
    $privatePatterns | Where-Object { $candidate -match $_ } | Select-Object -First 1
}
if ($private) { throw "User-owned or generated files are tracked: $($private -join ', ')" }

$appProject = [xml](Get-Content -LiteralPath (Join-Path $root 'src/NiiRMotion.App/NiiRMotion.App.csproj') -Raw)
$version = [string]($appProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
$version = $version.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Application version is invalid: $version" }

$notices = Get-Content -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Raw
foreach ($name in @('PSMoveAPI', 'Inter font', 'WiimoteLib', 'OpenVR', 'OpenXR', 'Inno Setup')) {
    if ($notices -notmatch [regex]::Escape($name)) { throw "Third-party notice is missing: $name" }
}

if ($Strict) {
    $status = @(& git -C $root status --short)
    $unsafe = $status | Where-Object { $_ -notmatch 'native/openvr-driver/dist/resources/input/niirmotion_profile\.json$' }
    if ($unsafe) { throw "Release tree has unreviewed changes: $($unsafe -join '; ')" }
}

Write-Host "Release readiness contracts passed for NiiMotion $version." -ForegroundColor Green
