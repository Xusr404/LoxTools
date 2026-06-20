param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$MsBuildPath = "",
    [string]$AppVersion = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "LoxTools\LoxTools.csproj"
$publishDir = Join-Path $repoRoot "artifacts\installer-input"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
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

if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-(?:(?:beta|rc)\.\d+|dev\.\d{8}\.\d+))?$') {
    throw "Unsupported version '$AppVersion'. Use MAJOR.MINOR.PATCH, beta.N, rc.N, or the CI-only dev.YYYYMMDD.N suffix."
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuildCommand) {
        $MsBuildPath = $msbuildCommand.Source
    }
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    $vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        $visualStudioPath = & $vswherePath -latest -requires Microsoft.Component.MSBuild -property installationPath
        if (-not [string]::IsNullOrWhiteSpace($visualStudioPath)) {
            $candidate = Join-Path $visualStudioPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) {
                $MsBuildPath = $candidate
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($MsBuildPath)) {
    throw "Visual Studio MSBuild was not found. Run from a Visual Studio Developer PowerShell or pass -MsBuildPath. The .NET SDK MSBuild cannot build this project because it uses a COM reference."
}

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& $MsBuildPath $projectPath `
    /t:Restore,Publish `
    /p:Configuration=$Configuration `
    /p:RuntimeIdentifier=$RuntimeIdentifier `
    /p:SelfContained=true `
    /p:PublishDir="$publishDir\" `
    /p:PublishSingleFile=false `
    /p:Version=$AppVersion

if ($LASTEXITCODE -ne 0) {
    throw "MSBuild publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDir "LoxTools.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Expected application was not published: $publishedExe"
}

Write-Host "Application published: $publishDir"
