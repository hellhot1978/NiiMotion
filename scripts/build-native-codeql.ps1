param([Parameter(Mandatory = $true)][string]$OpenVrSdkPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts\codeql-native'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$openVrInclude = Join-Path $OpenVrSdkPath 'headers'
$openVrLibrary = Join-Path $OpenVrSdkPath 'lib\win64'
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) { throw 'MSVC developer environment is not active.' }
if (-not (Test-Path -LiteralPath (Join-Path $openVrInclude 'openvr_driver.h'))) { throw 'Pinned OpenVR SDK is missing.' }
$driverOutput = Join-Path $output 'driver_niirmotion.dll'
$openXrOutput = Join-Path $output 'niirmotion_openxr.dll'
$openXrObject = Join-Path $output 'niirmotion_openxr.obj'
$overlayOutput = Join-Path $output 'NiiMotion.VrOverlay.exe'

& cl.exe /nologo /std:c++20 /EHsc /O2 /LD /I $openVrInclude (Join-Path $root 'native\openvr-driver\driver.cpp') /link "/OUT:$driverOutput" advapi32.lib
if ($LASTEXITCODE -ne 0) { throw 'CodeQL OpenVR driver build failed.' }
$clang = (Get-Command clang-cl.exe -ErrorAction SilentlyContinue).Source
$lld = (Get-Command lld-link.exe -ErrorAction SilentlyContinue).Source
if (-not $clang -or -not $lld) { throw 'LLVM compiler is unavailable for the freestanding OpenXR layer.' }
& $clang /nologo /c /O2 /GS- /GR- /EHs-c- /Zl /DWIN32 /D_WINDOWS /D_UNICODE /DUNICODE /I (Join-Path $root 'native\openxr-layer\include') (Join-Path $root 'native\openxr-layer\layer.cpp') "/Fo$openXrObject"
if ($LASTEXITCODE -ne 0) { throw 'CodeQL OpenXR layer compilation failed.' }
& $lld /nologo /dll /noentry /nodefaultlib /machine:x64 "/out:$openXrOutput" $openXrObject kernel32.lib
if ($LASTEXITCODE -ne 0) { throw 'CodeQL OpenXR layer link failed.' }
& cl.exe /nologo /std:c++20 /EHsc /O2 /DUNICODE /D_UNICODE /I $openVrInclude (Join-Path $root 'native\vr-overlay\overlay.cpp') /link /SUBSYSTEM:WINDOWS "/LIBPATH:$openVrLibrary" "/OUT:$overlayOutput" openvr_api.lib d3d11.lib dxgi.lib gdi32.lib user32.lib
if ($LASTEXITCODE -ne 0) { throw 'CodeQL SteamVR overlay build failed.' }
