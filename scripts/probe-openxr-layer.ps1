$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-openxr-layer.ps1') | Out-Null
$root = Split-Path $PSScriptRoot -Parent
$clang = 'C:\Program Files\LLVM\bin\clang-cl.exe'
$link = 'C:\Program Files\LLVM\bin\lld-link.exe'
$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\Lib' -Directory | Sort-Object Name -Descending | Select-Object -First 1
$include = Join-Path $root 'native\openxr-layer\include'
$source = Join-Path $root 'native\openxr-layer\probe.cpp'
$object = Join-Path $env:TEMP 'niirmotion-openxr-probe.obj'
$exe = Join-Path $env:TEMP 'niirmotion-openxr-probe.exe'
$manifest = Join-Path $root 'native\openxr-layer\dist\niirmotion_openxr.json'
$registry = 'HKCU:\Software\Khronos\OpenXR\1\ApiLayers\Implicit'
New-Item -Path $registry -Force | Out-Null
$previous = $null
try { $previous = Get-ItemPropertyValue -Path $registry -Name $manifest -ErrorAction Stop } catch { }
try {
    New-ItemProperty -Path $registry -Name $manifest -PropertyType DWord -Value 0 -Force | Out-Null
    & $clang /nologo /c /O2 /GS- /GR- /EHs-c- /Zl /DWIN32 /D_WINDOWS /I $include $source /Fo$object
    if ($LASTEXITCODE -ne 0) { throw 'OpenXR probe derlenemedi.' }
    & $link /nologo /subsystem:console /entry:ProbeStart /nodefaultlib /machine:x64 /out:$exe $object (Join-Path $sdk.FullName 'um\x64\kernel32.lib')
    if ($LASTEXITCODE -ne 0) { throw 'OpenXR probe bağlanamadı.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "SteamVR OpenXR loader katmanı keşfedemedi (kod $LASTEXITCODE)." }
    'OPENXR_LAYER_DISCOVERY_OK'
}
finally {
    if ($null -eq $previous) { Remove-ItemProperty -Path $registry -Name $manifest -ErrorAction SilentlyContinue } else { Set-ItemProperty -Path $registry -Name $manifest -Value $previous }
    Remove-Item -LiteralPath $object,$exe -Force -ErrorAction SilentlyContinue
}
