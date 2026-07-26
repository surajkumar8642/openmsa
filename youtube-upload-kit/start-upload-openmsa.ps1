param(
    [string]$VideoPath = "D:\suraj2\Pictures\openmsa-intro.mp4",
    [string]$CredentialsPath = ".\client_secret.json",
    [string]$ChromeProfile = "Default",
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$kitRoot = $PSScriptRoot

function Resolve-ChromeBinary {
    $candidates = @(
        "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "${env:LOCALAPPDATA}\Chromium\Application\chrome.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    return ""
}

function Resolve-ChromeUserDataDir {
    $candidates = @(
        "${env:LOCALAPPDATA}\Google\Chrome\User Data",
        "${env:LOCALAPPDATA}\Chromium\User Data"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    return ""
}

$ResolvedVideo = [System.IO.Path]::GetFullPath($VideoPath)
$ResolvedCredentials = if ([System.IO.Path]::IsPathRooted($CredentialsPath)) {
    $CredentialsPath
} else {
    Join-Path $kitRoot $CredentialsPath
}

if (-not (Test-Path $ResolvedVideo)) {
    throw "Video not found: $ResolvedVideo"
}
if (-not (Test-Path $ResolvedCredentials)) {
    throw "Credentials file not found: $ResolvedCredentials"
}

$chromeBinary = Resolve-ChromeBinary
$chromeUserDataDir = Resolve-ChromeUserDataDir

if (-not $chromeBinary) {
    Write-Host "Chrome executable not auto-detected; browser will use system default." -ForegroundColor Yellow
}
if (-not $chromeUserDataDir) {
    Write-Host "Chrome user-data directory not auto-detected; using token/profile defaults." -ForegroundColor Yellow
}

Set-Location $kitRoot
& .\upload-all.ps1 -VideoPath $ResolvedVideo -CredentialsPath $ResolvedCredentials -ChromeProfile $ChromeProfile -ChromeBinary $chromeBinary -ChromeUserDataDir $chromeUserDataDir -NoBrowser:$NoBrowser
