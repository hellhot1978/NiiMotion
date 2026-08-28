param(
    [switch]$SkipInstallerBuild,
    [switch]$SkipUiSmoke
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseDirectory = Join-Path $root 'artifacts\release'
$installerDirectory = Join-Path $root 'artifacts\installer'
$projectPath = Join-Path $root 'src\NiiRMotion.App\NiiRMotion.App.csproj'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
$version = $version.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid application version: $version" }

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-release-readiness.ps1') -Strict
if ($LASTEXITCODE -ne 0) { throw 'Strict release-readiness verification failed.' }

if (-not $SkipInstallerBuild) {
    $installerArguments = @('-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'build-installer.ps1'))
    if ($SkipUiSmoke) { $installerArguments += '-SkipUiSmoke' }
    & powershell @installerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
}

$installer = Join-Path $installerDirectory "NiiMotion-Setup-$version-x64.exe"
$installerChecksum = Join-Path $installerDirectory "NiiMotion-Setup-$version-x64.sha256"
foreach ($path in @($installer, $installerChecksum)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release asset is missing: $path" }
}

$expectedInstallerHash = ((Get-Content -LiteralPath $installerChecksum -Raw) -split '\s+')[0].ToLowerInvariant()
$actualInstallerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedInstallerHash -ne $actualInstallerHash) { throw 'Installer checksum does not match the release binary.' }

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-installer-smoke.ps1') -Installer $installer
if ($LASTEXITCODE -ne 0) { throw 'Installer lifecycle verification failed.' }

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$inventory = Join-Path $releaseDirectory 'component-inventory.json'
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'export-component-inventory.ps1') -Output $inventory
if ($LASTEXITCODE -ne 0) { throw 'Component inventory generation failed.' }

$assets = @($installer, $installerChecksum, $inventory) | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    [ordered]@{
        name = $item.Name
        sizeBytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Source commit could not be resolved.' }

$manifestPath = Join-Path $releaseDirectory 'release-candidate.json'
[ordered]@{
    schemaVersion = 1
    product = 'NiiMotion'
    version = $version
    sourceCommit = $commit
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    automatedVerification = [ordered]@{
        canonicalGate = 'passed'
        uiSmoke = if ($SkipUiSmoke) { 'not-run-headless-environment' } else { 'passed' }
        installerLifecycle = 'passed'
        hardwareAcceptance = 'pending-owner-validation'
        codeSigning = 'not-configured'
    }
    assets = $assets
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

$manifestChecksum = Join-Path $releaseDirectory 'release-candidate.sha256'
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $manifestChecksum -Encoding ascii -NoNewline -Value "$manifestHash  $(Split-Path -Leaf $manifestPath)"

Write-Host "Verified release candidate prepared for NiiMotion $version." -ForegroundColor Green
Write-Host "Manifest: $manifestPath" -ForegroundColor Green
Write-Host 'Hardware acceptance and code signing remain explicit external gates.' -ForegroundColor Yellow
