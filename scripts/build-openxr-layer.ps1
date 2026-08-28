param(
    [string]$OutputDirectory = '',
    [string]$ZigPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ZigPath)) { $ZigPath = Join-Path $root '.tools\zig-extract\zig-x86_64-windows-0.16.0\zig.exe' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'native\openxr-layer\dist\bin\win64' }
$source = Join-Path $root 'native\openxr-layer\layer.cpp'
$include = Join-Path $root 'native\openxr-layer\include'
if (-not (Test-Path -LiteralPath $ZigPath)) { throw "Zig compiler is missing: $ZigPath" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory 'niirmotion_openxr.dll'
& $ZigPath c++ $source -target x86_64-windows-gnu -std=c++20 -O2 -shared "-I$include" -o $output
if ($LASTEXITCODE -ne 0) { throw 'OpenXR layer build failed.' }
Write-Output $output
