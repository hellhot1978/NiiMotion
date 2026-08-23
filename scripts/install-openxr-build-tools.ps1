$ErrorActionPreference = 'Stop'
winget install --id LLVM.LLVM --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
if ($LASTEXITCODE -ne 0) { throw 'LLVM kurulamadı.' }
winget install --id Microsoft.WindowsSDK.10.0.18362 --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
if ($LASTEXITCODE -ne 0) { throw 'Windows SDK kurulamadı.' }
Write-Output 'OpenXR native derleme araçları hazır. İş bittikten sonra disk alanını geri kazanmak için kaldırılabilir.'
