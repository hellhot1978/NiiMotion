param([string]$Output = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if (Test-Path -LiteralPath (Join-Path $root '.dotnet\dotnet.exe')) { Join-Path $root '.dotnet\dotnet.exe' } else { 'dotnet' }
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root 'artifacts\release\component-inventory.json' }
$directory = Split-Path -Parent $Output
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$temporary = Join-Path $directory 'dotnet-packages.json'
& $dotnet list (Join-Path $root 'NiiRMotion.slnx') package --include-transitive --format json | Set-Content -LiteralPath $temporary -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Managed dependency inventory could not be generated.' }
$managed = Get-Content -LiteralPath $temporary -Raw | ConvertFrom-Json
$document = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    product = 'NiiMotion'
    managedDependencies = $managed
    nativeDependencies = @(
        [ordered]@{ name = 'OpenVR SDK'; version = '2.15.6'; source = 'https://github.com/ValveSoftware/openvr'; bundledRuntime = 'openvr_api.dll' },
        [ordered]@{ name = 'PSMoveAPI'; version = '4.0.12'; source = 'bundled offline runtime'; bundledRuntime = 'psmove.exe, psmoveapi.dll' },
        [ordered]@{ name = 'WiimoteLib.NetCore'; version = '1.0.0'; source = 'NuGet'; license = 'MIT' }
    )
}
$document | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Output -Encoding utf8
Remove-Item -LiteralPath $temporary -Force
Write-Host "Component inventory written: $Output" -ForegroundColor Green
