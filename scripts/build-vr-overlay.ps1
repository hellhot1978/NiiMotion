$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$zig = Join-Path $root '.tools\zig-extract\zig-x86_64-windows-0.16.0\zig.exe'
$sdk = Join-Path $root '.tools\openvr-extract\openvr-2.15.6'
$source = Join-Path $root 'native\vr-overlay\overlay.cpp'
$output = Join-Path $root 'native\vr-overlay\dist'
if (-not (Test-Path -LiteralPath $zig)) { throw 'Zig compiler is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'headers\openvr.h'))) { throw 'OpenVR SDK is missing.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $zig c++ $source -target x86_64-windows-gnu -std=c++20 -O2 -municode -mwindows "-I$(Join-Path $sdk 'headers')" "-L$(Join-Path $sdk 'lib\win64')" -lopenvr_api -ld3d11 -ldxgi -lgdi32 -luser32 -o (Join-Path $output 'NiiMotion.VrOverlay.exe')
if ($LASTEXITCODE -ne 0) { throw 'VR overlay build failed.' }
Copy-Item -LiteralPath (Join-Path $sdk 'bin\win64\openvr_api.dll') -Destination (Join-Path $output 'openvr_api.dll') -Force
