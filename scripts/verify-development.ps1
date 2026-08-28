param(
    [switch]$Publish,
    [switch]$UiSmoke
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }

$drive = Get-PSDrive -Name C
if ($drive.Free -lt 10GB) { throw "At least 10 GB free space is required on C:. Available: $([math]::Round($drive.Free / 1GB, 2)) GB" }

Push-Location $projectRoot
try {
    & $dotnet build NiiRMotion.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    & $dotnet run --project tests\NiiRMotion.Tests\NiiRMotion.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }

    & $dotnet run --project tools\NiiMotion.LocalizationAudit\NiiMotion.LocalizationAudit.csproj -c Release -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'English localization audit failed.' }

    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-release-readiness.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Release readiness contracts failed.' }

    if ($UiSmoke) {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify-ui.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'UI smoke verification failed.' }
    }

    if ($Publish) {
        $output = Join-Path $projectRoot 'artifacts\app'
        Get-Process NiiRMotion.App -ErrorAction SilentlyContinue | Stop-Process
        & $dotnet publish src\NiiRMotion.App\NiiRMotion.App.csproj -c Release -r win-x64 --self-contained true -o $output --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

        & $dotnet run --project tests\NiiRMotion.Tests\NiiRMotion.Tests.csproj -c Release --no-build -- "--release-manifest=$output"
        if ($LASTEXITCODE -ne 0) { throw 'Release manifest creation failed.' }

        $required = @(
            'NiiRMotion.App.exe', 'coreclr.dll', 'hostfxr.dll',
            'Models', 'Calibration',
            'OpenVRDriver\driver.vrdrivermanifest', 'OpenVRDriver\bin\win64\driver_niirmotion.dll',
            'OpenXRLayer\niirmotion_openxr.json', 'OpenXRLayer\bin\win64\niirmotion_openxr.dll',
            'VrOverlay\NiiMotion.VrOverlay.exe', 'VrOverlay\openvr_api.dll', 'VrOverlay\niirmotion.vrmanifest'
        )
        $missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $output $_)) }
        if ($missing) { throw "Published package is incomplete: $($missing -join ', ')" }
        Write-Host "Standalone package verified: $output" -ForegroundColor Green
    }

    $projectBytes = (Get-ChildItem $projectRoot -File -Recurse -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if ($projectBytes -gt 15GB) { throw "Project exceeds 15 GB: $([math]::Round($projectBytes / 1GB, 2)) GB" }
    Write-Host "NiiMotion verification passed. Project $([math]::Round($projectBytes / 1GB, 2)) GB; C: free $([math]::Round((Get-PSDrive C).Free / 1GB, 2)) GB." -ForegroundColor Green
}
finally {
    Pop-Location
}
