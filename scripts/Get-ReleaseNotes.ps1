param(
    [Parameter(Mandatory = $true)]
    [string]$AppVersion,
    [string]$ChangelogPath = "",
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if ($AppVersion -notmatch '^\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$') {
    throw "Unsupported release version '$AppVersion'."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $ChangelogPath = Join-Path $repoRoot "CHANGELOG.md"
}

if (-not (Test-Path $ChangelogPath)) {
    throw "Changelog not found: $ChangelogPath"
}

$lines = Get-Content -LiteralPath $ChangelogPath
$escapedVersion = [regex]::Escape($AppVersion)
$headingPattern = "^## \[$escapedVersion\] - \d{4}-\d{2}-\d{2}$"
$headingIndexes = for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match $headingPattern) {
        $index
    }
}

if ($headingIndexes.Count -ne 1) {
    throw "Expected exactly one changelog heading for version '$AppVersion', found $($headingIndexes.Count)."
}

$startIndex = $headingIndexes[0] + 1
$endIndex = $lines.Count
for ($index = $startIndex; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '^## \[') {
        $endIndex = $index
        break
    }
}

if ($endIndex -le $startIndex) {
    throw "Changelog section for version '$AppVersion' is empty."
}

$releaseNotes = ($lines[$startIndex..($endIndex - 1)] -join [Environment]::NewLine).Trim()
if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
    throw "Changelog section for version '$AppVersion' is empty."
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $releaseNotes -Encoding utf8NoBOM
Write-Host "Release notes written: $OutputPath"
