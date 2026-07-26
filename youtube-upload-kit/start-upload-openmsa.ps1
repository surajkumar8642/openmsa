param(
    [string]$VideoPath = "D:\suraj2\Pictures\openmsa-intro.mp4",
    [string]$CredentialsPath = ".\client_secret.json",
    [string]$ChromeBinary = "",
    [string]$ChromeUserDataDir = "",
    [string]$ChromeProfile = "",
    [switch]$Interactive,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$kitRoot = $PSScriptRoot

function Resolve-ChromeBinary {
    $candidates = @(
        "${env:LOCALAPPDATA}\Chromium\Application\chrome.exe",
        "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "${env:LOCALAPPDATA}\Google\Chrome\Application\chrome.exe"
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
        "${env:LOCALAPPDATA}\Chromium\User Data",
        "${env:LOCALAPPDATA}\Google\Chrome\User Data"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    return ""
}

function Get-ActiveProfileName {
    param([string]$UserDataDir)
    $statePath = Join-Path $UserDataDir 'Local State'
    if (-not (Test-Path $statePath)) { return $null }
    try {
        $state = Get-Content $statePath -Raw | ConvertFrom-Json
        $active = $state.profile.last_active_profiles
        if ($active -and $active.Count -gt 0) { return [string]$active[0] }
        $info = $state.profile.info_cache
        foreach ($key in $info.PSObject.Properties.Name) {
            $value = $info.$key
            if ($value.is_default) { return [string]$key }
        }
    } catch {
        return $null
    }
    return $null
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

$chromeBinary = if ($ChromeBinary) { $ChromeBinary } else { Resolve-ChromeBinary }
$chromeUserDataDir = if ($ChromeUserDataDir) { $ChromeUserDataDir } else { Resolve-ChromeUserDataDir }
$detectedProfile = if ($chromeUserDataDir) { Get-ActiveProfileName -UserDataDir $chromeUserDataDir } else { $null }

if (-not $ChromeProfile) {
    if ($detectedProfile) {
        $ChromeProfile = $detectedProfile
    } else {
        $ChromeProfile = "Default"
    }
}

if (-not $chromeBinary) {
    Write-Host "Chrome executable not auto-detected; browser will use system default." -ForegroundColor Yellow
}
if (-not $chromeUserDataDir) {
    Write-Host "Chrome user-data directory not auto-detected; using token/profile defaults." -ForegroundColor Yellow
} else {
    Write-Host "Using browser profile directory: $chromeUserDataDir" -ForegroundColor Cyan
    Write-Host "Detected active profile: $ChromeProfile" -ForegroundColor Cyan
}

Set-Location $kitRoot
& .\upload-all.ps1 -VideoPath $ResolvedVideo -CredentialsPath $ResolvedCredentials -ChromeProfile $ChromeProfile -ChromeBinary $chromeBinary -ChromeUserDataDir $chromeUserDataDir -NoBrowser:$NoBrowser -Interactive:$Interactive
