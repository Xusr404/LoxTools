param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [string]$ExpectedPublisher = "",
    [string]$TimestampUrl = "https://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
    throw "Signing target not found: $FilePath"
}

$certificateBase64 = $env:LOXTOOLS_SIGNING_PFX_BASE64
$certificatePassword = $env:LOXTOOLS_SIGNING_PFX_PASSWORD
$hasCertificate = -not [string]::IsNullOrWhiteSpace($certificateBase64)
$hasPublisher = -not [string]::IsNullOrWhiteSpace($ExpectedPublisher)

if (-not $hasCertificate -and -not $hasPublisher) {
    Write-Host "Signing is not configured; leaving $(Split-Path -Leaf $FilePath) unsigned."
    return
}

if (-not $hasCertificate -or [string]::IsNullOrWhiteSpace($certificatePassword) -or -not $hasPublisher) {
    throw "Signing configuration is incomplete. Certificate, password, and expected publisher must be configured together."
}

$signtool = Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -File |
    Where-Object { $_.DirectoryName -like '*\x64' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signtool) {
    throw "signtool.exe was not found."
}

$temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$certificatePath = Join-Path $temporaryRoot ("LoxTools-signing-{0}.pfx" -f [Guid]::NewGuid().ToString("N"))
try {
    [IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($certificateBase64))
    & $signtool.FullName sign /fd SHA256 /f $certificatePath /p $certificatePassword /tr $TimestampUrl /td SHA256 $FilePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed with exit code $LASTEXITCODE."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $FilePath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Signature verification failed: $($signature.StatusMessage)"
    }

    $actualPublisher = $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    if (-not [string]::Equals($actualPublisher, $ExpectedPublisher, [StringComparison]::Ordinal)) {
        throw "Signing publisher '$actualPublisher' does not match expected publisher '$ExpectedPublisher'."
    }
} finally {
    if (Test-Path -LiteralPath $certificatePath) {
        Remove-Item -LiteralPath $certificatePath -Force
    }
}
