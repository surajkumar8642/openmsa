param(
    [string]$ShortcutName = "OpenMSA YouTube Upload",
    [switch]$DesktopOnly,
    [switch]$StartMenuOnly
)

$ErrorActionPreference = "Stop"
$kitRoot = $PSScriptRoot
$launcher = Join-Path $kitRoot "start-upload-openmsa.cmd"
$powershell = (Get-Command powershell.exe).Source

$args = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $kitRoot "start-upload-openmsa.ps1") + '"'

$desktop = [Environment]::GetFolderPath('Desktop')
$startMenuRoot = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuPath = Join-Path $startMenuRoot "OpenMSA"

if (-not (Test-Path $startMenuPath)) {
    New-Item -ItemType Directory -Path $startMenuPath | Out-Null
}

function New-Shortcut([string]$ShortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $powershell
    $shortcut.Arguments = $args
    $shortcut.WorkingDirectory = $kitRoot
    if (Test-Path $launcher) {
      $shortcut.IconLocation = (Get-Item (Get-Command chrome.exe -ErrorAction SilentlyContinue).Source -ErrorAction SilentlyContinue).Path
    }
    $shortcut.Save()
}

if (-not $DesktopOnly) {
    New-Shortcut (Join-Path $startMenuPath "$ShortcutName.lnk")
    Write-Host "Created start menu shortcut: $startMenuPath\$ShortcutName.lnk"
}

if (-not $StartMenuOnly) {
    $desktopShortcut = Join-Path $desktop "$ShortcutName.lnk"
    New-Shortcut $desktopShortcut
    Write-Host "Created desktop shortcut: $desktopShortcut"
}
