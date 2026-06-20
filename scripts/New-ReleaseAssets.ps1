param(
    [Parameter(Mandatory = $true)]
    [string]$AppVersion,
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-(?:(?:beta|rc)\.\d+|dev\.\d{8}\.\d+))?$') {
    throw "Unsupported version '$AppVersion'."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts\installer-input"
$installerPath = Join-Path $repoRoot "artifacts\installer\LoxTools-Setup-$AppVersion.exe"
$releaseDir = Join-Path $repoRoot "artifacts\release"
$portablePath = Join-Path $releaseDir "LoxTools-Portable-$AppVersion-$RuntimeIdentifier.zip"
$checksumPath = Join-Path $releaseDir "SHA256SUMS.txt"

if (-not (Test-Path (Join-Path $publishDir "LoxTools.exe"))) {
    throw "Published application not found in $publishDir."
}

if (-not (Test-Path $installerPath)) {
    throw "Installer not found: $installerPath"
}

if (Test-Path $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$releaseInstallerPath = Join-Path $releaseDir (Split-Path -Leaf $installerPath)
Copy-Item -LiteralPath $installerPath -Destination $releaseInstallerPath
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portablePath -CompressionLevel Optimal

$assetPaths = @($releaseInstallerPath, $portablePath)
$checksumLines = foreach ($assetPath in $assetPaths) {
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $assetPath)"
}

Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding utf8NoBOM
Write-Host "Release assets created: $releaseDir"
