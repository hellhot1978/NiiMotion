param(
    [string]$OutputDirectory = '',
    [string]$ZigPath = '',
    [string]$OpenVrSdkPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ZigPath)) { $ZigPath = Join-Path $root '.tools\zig-extract\zig-x86_64-windows-0.16.0\zig.exe' }
if ([string]::IsNullOrWhiteSpace($OpenVrSdkPath)) { $OpenVrSdkPath = Join-Path $root '.tools\openvr-extract\openvr-2.15.6' }
$source = Join-Path $root 'native\vr-overlay\overlay.cpp'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'native\vr-overlay\dist' }
if (-not (Test-Path -LiteralPath $ZigPath)) { throw "Zig compiler is missing: $ZigPath" }
if (-not (Test-Path -LiteralPath (Join-Path $OpenVrSdkPath 'headers\openvr.h'))) { throw "OpenVR SDK is missing: $OpenVrSdkPath" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $ZigPath c++ $source -target x86_64-windows-gnu -std=c++20 -O2 -municode -mwindows "-I$(Join-Path $OpenVrSdkPath 'headers')" "-L$(Join-Path $OpenVrSdkPath 'lib\win64')" -lopenvr_api -ld3d11 -ldxgi -lgdi32 -luser32 -o (Join-Path $OutputDirectory 'NiiMotion.VrOverlay.exe')
if ($LASTEXITCODE -ne 0) { throw 'VR overlay build failed.' }
Copy-Item -LiteralPath (Join-Path $OpenVrSdkPath 'bin\win64\openvr_api.dll') -Destination (Join-Path $OutputDirectory 'openvr_api.dll') -Force
