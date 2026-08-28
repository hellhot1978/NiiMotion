$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$publish = Join-Path $root 'artifacts\app'
$installerOutput = Join-Path $root 'artifacts\installer'
$appProject = Join-Path $root 'src\NiiRMotion.App\NiiRMotion.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $appProject
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid application version in NiiRMotion.App.csproj: $version" }
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-development.ps1') -Publish -UiSmoke
if ($LASTEXITCODE -ne 0) { throw 'Verified NiiMotion publish failed.' }
if (-not $iscc) { throw 'Inno Setup 6 bulunamadı. Önce scripts/install-build-tools.ps1 çalıştır.' }
& $iscc "/DMyAppVersion=$version" (Join-Path $root 'installer\NiiMotion.iss')
if ($LASTEXITCODE -ne 0) { throw 'NiiMotion installer build failed.' }

$installer = Join-Path $installerOutput "NiiMotion-Setup-$version-x64.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer output is missing: $installer" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = Join-Path $installerOutput "NiiMotion-Setup-$version-x64.sha256"
Set-Content -LiteralPath $checksum -Encoding ascii -NoNewline -Value "$hash  $(Split-Path -Leaf $installer)"
Write-Host "Installer verified: $installer" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
