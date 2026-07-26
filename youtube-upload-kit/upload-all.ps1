param(
    [Parameter(Mandatory = $true)]
    [string]$VideoPath,

    [Parameter(Mandatory = $true)]
    [string]$CredentialsPath,

    [string]$ChromeBinary = "",
    [string]$ChromeUserDataDir = "",
    [string]$ChromeProfile = "Default",
    [switch]$NoBrowser,
    [switch]$UseDefaultChromeProfile = $true
)

$resolveChromeProfile = {
    param([string]$PathCandidate)
    if (-not $PathCandidate) { return "" }
    if (Test-Path $PathCandidate) { return $PathCandidate }
    return ""
}

if ($UseDefaultChromeProfile) {
    $chromeBinary = ""
    $resolvedUserDataDir = ""

    $candidate = $resolveChromeProfile.Invoke("${env:ProgramFiles}\Google\Chrome\Application\chrome.exe")
    if ($candidate) { $chromeBinary = $candidate }
    if (-not $chromeBinary) {
      $candidate = $resolveChromeProfile.Invoke("${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe")
      if ($candidate) { $chromeBinary = $candidate }
    }
    if (-not $chromeBinary) {
      $candidate = $resolveChromeProfile.Invoke("${env:LOCALAPPDATA}\Chromium\Application\chrome.exe")
      if ($candidate) { $chromeBinary = $candidate }
    }
    if (-not $ChromeBinary -and $chromeBinary) {
        $ChromeBinary = $chromeBinary
    }

    $resolvedProfileDir = $resolveChromeProfile.Invoke("${env:LOCALAPPDATA}\Google\Chrome\User Data")
    if ($resolvedProfileDir) { $resolvedUserDataDir = $resolvedProfileDir }
    if (-not $resolvedProfileDir) {
      $resolvedProfileDir = $resolveChromeProfile.Invoke("${env:LOCALAPPDATA}\Chromium\User Data")
      if ($resolvedProfileDir) { $resolvedUserDataDir = $resolvedProfileDir }
    }
    if (-not $ChromeUserDataDir -and $resolvedUserDataDir) {
      $ChromeUserDataDir = $resolvedUserDataDir
    }

    if ($ChromeBinary) {
        Write-Host "Using Chrome executable: $ChromeBinary" -ForegroundColor Cyan
    }
    if ($ChromeUserDataDir) {
        Write-Host "Using Chrome user-data dir: $ChromeUserDataDir" -ForegroundColor Cyan
    }
}

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
