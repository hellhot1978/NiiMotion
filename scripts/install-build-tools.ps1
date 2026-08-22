$ErrorActionPreference = 'Stop'
winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup kurulamadı.' }
