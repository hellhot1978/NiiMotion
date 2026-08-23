$ErrorActionPreference = 'Stop'
$clang = 'C:\Program Files\LLVM\bin\clang-cl.exe'
$link = 'C:\Program Files\LLVM\bin\lld-link.exe'
$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\Lib' -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not (Test-Path $clang) -or -not $sdk) { throw 'LLVM veya Windows SDK bulunamadı.' }
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'native\openxr-layer\layer.cpp'
$include = Join-Path $root 'native\openxr-layer\include'
$object = Join-Path $root 'native\openxr-layer\layer.obj'
$output = Join-Path $root 'native\openxr-layer\dist\bin\win64\niirmotion_openxr.dll'
& $clang /nologo /c /O2 /GS- /GR- /EHs-c- /Zl /DWIN32 /D_WINDOWS /D_UNICODE /DUNICODE /I $include $source /Fo$object
if ($LASTEXITCODE -ne 0) { throw 'OpenXR katmanı derlenemedi.' }
$um = Join-Path $sdk.FullName 'um\x64'
& $link /nologo /dll /noentry /nodefaultlib /machine:x64 /out:$output $object (Join-Path $um 'kernel32.lib')
if ($LASTEXITCODE -ne 0) { throw 'OpenXR katmanı bağlanamadı.' }
Remove-Item -LiteralPath $object -Force
Write-Output $output
