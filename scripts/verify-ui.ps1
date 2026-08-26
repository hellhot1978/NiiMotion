param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$app = Join-Path $projectRoot 'src\NiiRMotion.App\bin\Release\net10.0-windows\win-x64\NiiRMotion.App.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Release app is missing. Run scripts\verify-development.ps1 first.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot 'artifacts\ui-smoke' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$cases = @(
    @{ Name = 'overview-tr-compact'; Language = 'tr'; Size = '1100x700' },
    @{ Name = 'overview-en-compact'; Language = 'en'; Size = '1100x700' },
    @{ Name = 'overview-tr-standard'; Language = 'tr'; Size = '1200x760' },
    @{ Name = 'overview-en-standard'; Language = 'en'; Size = '1200x760' }
)

foreach ($case in $cases) {
    $path = Join-Path $OutputDirectory ($case.Name + '.png')
    $auditPath = Join-Path $OutputDirectory ($case.Name + '.txt')
    [System.IO.File]::Delete($path)
    [System.IO.File]::Delete($auditPath)
    $previousLanguage = $env:NIIRMOTION_UI_LANGUAGE
    $env:NIIRMOTION_UI_LANGUAGE = $case.Language
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = $app
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.Arguments = "--ui-language=$($case.Language) --window-size=$($case.Size) --ui-language-audit=`"$auditPath`" --screenshot=`"$path`""
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $process.WaitForExit()
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while ((-not (Test-Path -LiteralPath $path) -or -not (Test-Path -LiteralPath $auditPath)) -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
        }
    }
    finally {
        $env:NIIRMOTION_UI_LANGUAGE = $previousLanguage
    }
    if ($process.ExitCode -ne 0) { throw "UI smoke case failed: $($case.Name)" }
    if (-not (Test-Path -LiteralPath $path)) { throw "UI screenshot was not created: $($case.Name)" }
    $expectedLanguage = if ($case.Language -eq 'en') { 'english=True' } else { 'english=False' }
    $auditText = if (Test-Path -LiteralPath $auditPath) { [System.IO.File]::ReadAllText($auditPath, [System.Text.Encoding]::UTF8) } else { '' }
    if (-not $auditText.Contains($expectedLanguage)) {
        throw "UI language was not applied: $($case.Name)"
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 10000 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71) {
        throw "UI screenshot is invalid or empty: $($case.Name)"
    }
}

$compactTurkish = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutputDirectory 'overview-tr-compact.png')).Hash
$compactEnglish = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutputDirectory 'overview-en-compact.png')).Hash
if ($compactTurkish -eq $compactEnglish) { throw 'Turkish and English UI renders are unexpectedly identical.' }

foreach ($language in @('tr', 'en')) {
    $path = Join-Path $OutputDirectory "getting-started-$language.png"
    [System.IO.File]::Delete($path)
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $app
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = "--ui-language=$language --getting-started-screenshot=`"$path`""
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -lt 10000) { throw "Getting Started UI render failed: $language" }
}
$guideTurkish = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutputDirectory 'getting-started-tr.png')).Hash
$guideEnglish = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $OutputDirectory 'getting-started-en.png')).Hash
if ($guideTurkish -eq $guideEnglish) { throw 'Getting Started language renders are unexpectedly identical.' }

Write-Host "UI smoke verification passed: $($cases.Count) viewport cases and 2 Getting Started renders." -ForegroundColor Green
