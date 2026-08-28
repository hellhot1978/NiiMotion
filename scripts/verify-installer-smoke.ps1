param(
    [Parameter(Mandatory = $true)]
    [string]$Installer,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$root = Split-Path -Parent $PSScriptRoot
$workspace = Join-Path $root 'artifacts\installer-smoke'
$installDirectory = Join-Path $workspace 'installed-app'
$isolatedProfile = Join-Path $workspace 'user-profile'
$screenshot = Join-Path $workspace 'first-launch.png'
$audit = Join-Path $workspace 'first-launch.txt'

if (Test-Path -LiteralPath $workspace) {
    Remove-Item -LiteralPath $workspace -Recurse -Force
}
New-Item -ItemType Directory -Path $installDirectory, $isolatedProfile -Force | Out-Null

function Invoke-CheckedProcess {
    param([string]$FileName, [string]$Arguments, [hashtable]$Environment = @{})
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($entry in $Environment.GetEnumerator()) { $startInfo.Environment[$entry.Key] = [string]$entry.Value }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Process failed with exit code $($process.ExitCode): $FileName" }
}

try {
    Invoke-CheckedProcess $installerPath "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LANG=english /DIR=`"$installDirectory`""

    $app = Join-Path $installDirectory 'NiiRMotion.App.exe'
    $runtimeConfig = Join-Path $installDirectory 'NiiRMotion.App.runtimeconfig.json'
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    foreach ($required in @($app, $runtimeConfig, $uninstaller)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Installed file is missing: $required" }
    }
    $runtime = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.framework) { throw 'Installed application is framework-dependent; standalone acceptance failed.' }

    $environment = @{
        APPDATA = Join-Path $isolatedProfile 'Roaming'
        LOCALAPPDATA = Join-Path $isolatedProfile 'Local'
        USERPROFILE = $isolatedProfile
        NIIRMOTION_UI_LANGUAGE = 'en'
    }
    foreach ($directory in @($environment.APPDATA, $environment.LOCALAPPDATA)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    Invoke-CheckedProcess $app "--ui-language=en --window-size=1100x700 --ui-language-audit=`"$audit`" --screenshot=`"$screenshot`"" $environment
    if (-not (Test-Path -LiteralPath $screenshot) -or (Get-Item -LiteralPath $screenshot).Length -lt 10000) { throw 'Installed application did not produce a valid first-launch render.' }
    if (-not (Test-Path -LiteralPath $audit) -or -not (Get-Content -LiteralPath $audit -Raw).Contains('english=True')) { throw 'Installed application did not honor the English first-launch setting.' }

    $sentinelDirectory = Join-Path $isolatedProfile 'Local\NiiMotion'
    New-Item -ItemType Directory -Path $sentinelDirectory -Force | Out-Null
    $sentinel = Join-Path $sentinelDirectory 'personal-data-preservation.txt'
    Set-Content -LiteralPath $sentinel -Value 'preserve' -Encoding ascii

    Invoke-CheckedProcess $uninstaller '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    if (Test-Path -LiteralPath $app) { throw 'Application executable remained after uninstall.' }
    if (-not (Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed user-owned data.' }

    Write-Host 'Installer smoke verification passed: silent install, standalone launch, English render, uninstall and personal-data preservation.' -ForegroundColor Green
}
finally {
    if (-not $KeepWorkspace -and (Test-Path -LiteralPath $workspace)) {
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }
}
