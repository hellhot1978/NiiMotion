param(
    [string]$OutputDirectory = '',
    [string]$ZigPath = '',
    [string]$OpenVrSdkPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ZigPath)) { $ZigPath = Join-Path $root '.tools\zig-extract\zig-x86_64-windows-0.16.0\zig.exe' }
if ([string]::IsNullOrWhiteSpace($OpenVrSdkPath)) { $OpenVrSdkPath = Join-Path $root '.tools\openvr-extract\openvr-2.15.6' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'native\openvr-driver\dist\bin\win64' }
$source = Join-Path $root 'native\openvr-driver\driver.cpp'
$header = Join-Path $OpenVrSdkPath 'headers\openvr_driver.h'
if (-not (Test-Path -LiteralPath $ZigPath)) { throw "Zig compiler is missing: $ZigPath" }
if (-not (Test-Path -LiteralPath $header)) { throw "OpenVR SDK is missing: $header" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory 'driver_niirmotion.dll'
& $ZigPath c++ $source -target x86_64-windows-gnu -std=c++20 -O2 -shared "-I$(Join-Path $OpenVrSdkPath 'headers')" -ladvapi32 -o $output
if ($LASTEXITCODE -ne 0) { throw 'OpenVR driver build failed.' }
Write-Output $output
