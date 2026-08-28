param(
    [string]$ZigPath = '',
    [string]$OpenVrSdkPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts\native-rebuild'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$common = @{}
if (-not [string]::IsNullOrWhiteSpace($ZigPath)) { $common.ZigPath = $ZigPath }
if (-not [string]::IsNullOrWhiteSpace($OpenVrSdkPath)) { $common.OpenVrSdkPath = $OpenVrSdkPath }

& (Join-Path $PSScriptRoot 'build-openvr-driver.ps1') -OutputDirectory (Join-Path $output 'OpenVRDriver') @common | Out-Null
$openXrArgs = @{ OutputDirectory = Join-Path $output 'OpenXRLayer' }
if ($common.ContainsKey('ZigPath')) { $openXrArgs.ZigPath = $common.ZigPath }
& (Join-Path $PSScriptRoot 'build-openxr-layer.ps1') @openXrArgs | Out-Null
& (Join-Path $PSScriptRoot 'build-vr-overlay.ps1') -OutputDirectory (Join-Path $output 'VrOverlay') @common

$expected = @(
    (Join-Path $output 'OpenVRDriver\driver_niirmotion.dll'),
    (Join-Path $output 'OpenXRLayer\niirmotion_openxr.dll'),
    (Join-Path $output 'VrOverlay\NiiMotion.VrOverlay.exe'),
    (Join-Path $output 'VrOverlay\openvr_api.dll')
)
foreach ($file in $expected) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Native rebuild output is missing: $file" }
    $bytes = [System.IO.File]::ReadAllBytes($file)
    if ($bytes.Length -lt 4096 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "Native rebuild output is not a valid PE image: $file" }
}
Write-Host 'Native rebuild verification passed: OpenVR driver, OpenXR layer and SteamVR overlay.' -ForegroundColor Green
