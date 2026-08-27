$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$publish = Join-Path $root 'artifacts\app'
$appProject = Join-Path $root 'src\NiiRMotion.App\NiiRMotion.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $appProject
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid application version in NiiRMotion.App.csproj: $version" }
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

& $dotnet publish $appProject -c Release -o $publish --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'NiiMotion publish failed.' }
if (-not $iscc) { throw 'Inno Setup 6 bulunamadı. Önce scripts/install-build-tools.ps1 çalıştır.' }
& $iscc "/DMyAppVersion=$version" (Join-Path $root 'installer\NiiMotion.iss')
if ($LASTEXITCODE -ne 0) { throw 'NiiMotion installer build failed.' }
