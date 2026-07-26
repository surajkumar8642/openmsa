param(
    [Parameter(Mandatory = $true)]
    [string]$VideoPath,

    [Parameter(Mandatory = $true)]
    [string]$CredentialsPath,

    [string]$ChromeBinary = "",
    [string]$ChromeUserDataDir = "",
    [string]$ChromeProfile = "Default",
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$kitRoot = $PSScriptRoot

$metadataFiles = @(
    (Join-Path $kitRoot "channel-1.json"),
    (Join-Path $kitRoot "channel-2.json")
)

foreach ($metadataFile in $metadataFiles) {
    Write-Host "=== Uploading via metadata: $metadataFile ===" -ForegroundColor Cyan
    $nodeScript = Join-Path $kitRoot "upload.mjs"
    $argsList = @(
        $nodeScript,
        "--video", $VideoPath,
        "--metadata", $metadataFile,
        "--credentials", $CredentialsPath,
        "--chrome-profile", $ChromeProfile
    )
    if ($ChromeBinary) {
        $argsList += "--chrome-binary"
        $argsList += $ChromeBinary
    }
    if ($ChromeUserDataDir) {
        $argsList += "--user-data-dir"
        $argsList += $ChromeUserDataDir
    }
    if ($NoBrowser) {
        $argsList += "--no-browser"
    }

    Write-Host "node $($argsList -join ' ')" -ForegroundColor Yellow
    Set-Location $kitRoot
    & node @argsList
    if ($LASTEXITCODE -ne 0) { throw "Upload failed for $metadataFile. Exit code: $LASTEXITCODE" }
    Start-Sleep -Seconds 2
}
