param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$InnoSetupCompiler = "",
    [string]$MsBuildPath = "",
    [string]$AppVersion = "",
    [string]$UpdatePublisher = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "LoxTools\LoxTools.csproj"
$installerScript = Join-Path $repoRoot "installer\LoxTools.iss"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishDir = Join-Path $artifactsRoot "installer-input"
$installerOutputDir = Join-Path $artifactsRoot "installer"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (-not (Test-Path $installerScript)) {
    throw "Installer script not found: $installerScript"
}

if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    [xml]$projectXml = Get-Content -LiteralPath $projectPath
    $AppVersion = $projectXml.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw "Version is missing from $projectPath"
}

if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-(?:(?:alpha|beta|rc)\.\d+|dev\.\d{8}\.\d+))?$') {
    throw "Unsupported version '$AppVersion'. Use MAJOR.MINOR.PATCH, alpha.N, beta.N, rc.N, or the CI-only dev.YYYYMMDD.N suffix."
}

if (Test-Path $installerOutputDir) {
    Remove-Item -LiteralPath $installerOutputDir -Recurse -Force
}

if (-not $SkipPublish) {
    $publishScript = Join-Path $PSScriptRoot "Publish-Application.ps1"
    & $publishScript `
        -Configuration $Configuration `
        -RuntimeIdentifier $RuntimeIdentifier `
        -MsBuildPath $MsBuildPath `
        -AppVersion $AppVersion `
        -UpdatePublisher $UpdatePublisher

    if ($LASTEXITCODE -ne 0) {
        throw "Application publish failed with exit code $LASTEXITCODE."
    }
} elseif (-not (Test-Path (Join-Path $publishDir "LoxTools.exe"))) {
    throw "Published application not found in $publishDir. Run Publish-Application.ps1 first or omit -SkipPublish."
}

New-Item -ItemType Directory -Force -Path $installerOutputDir | Out-Null

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand) {
        $InnoSetupCompiler = $isccCommand.Source
    } else {
        $defaultPaths = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        )

        $InnoSetupCompiler = $defaultPaths |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
            Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path $InnoSetupCompiler)) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6 or pass -InnoSetupCompiler."
}

& $InnoSetupCompiler `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$installerOutputDir" `
    "/DAppVersion=$AppVersion" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $installerOutputDir "LoxTools-Setup-$AppVersion.exe"
if (-not (Test-Path $setupPath)) {
    throw "Expected installer was not created: $setupPath"
}

Write-Host "Installer created: $setupPath"
