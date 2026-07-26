# OpenMSA YouTube Upload Kit

This kit uploads `openmsa-intro.mp4` to two YouTube channels and reuses your default Chrome/Chromium profile the way you use the browser normally.

## What is included

- `upload.mjs` – uploader using YouTube Data API v3
- `upload-all.ps1` – uploads to both channels (channel-1 + channel-2)
- `start-upload-openmsa.ps1` – one-click PowerShell launcher using auto-detected Chrome profile
- `start-upload-openmsa.cmd` – double-click/Start Menu friendly launcher
- `create-openmsa-upload-shortcut.ps1` – build desktop/start menu shortcuts
- `channel-1.json`, `channel-2.json` – per-channel metadata
- `materials-checklist.md` – required content checklist
- `tokens/` – created automatically after OAuth (not committed)
- `assets/` – optional thumbnails

## Why credentials are still required

YouTube OAuth login must happen once in your browser per channel (the token is reused after that). The tool itself cannot access your YouTube account directly from this environment.

## Setup

From `openmsa/youtube-upload-kit`:

```powershell
npm install
```

1. In Google Cloud, create OAuth credentials:
   - API: YouTube Data API v3
   - OAuth type: Desktop app (recommended) or Web app
   - Redirect URI: `http://localhost:8787/oauth2callback`
2. Save the downloaded JSON file as `client_secret.json` in this folder.
3. Edit metadata titles/tags/visibility in `channel-1.json` and `channel-2.json`.

## Run with Chrome-style profile (recommended)

Default behavior now tries to use Chrome user settings automatically:
- Chrome path auto-detected from `Program Files`
- User profile data dir auto-detected from `%LOCALAPPDATA%\Google\Chrome\User Data`
- Profile name defaults to `Default`

Quick default run (uses auto-detected Chrome profile):

```powershell
.\start-upload-openmsa.ps1
```

Quick double-click style run (default interactive per channel):

```powershell
.\start-upload-openmsa.ps1 -Interactive
```

If you keep the video in a different location:

```powershell
.\start-upload-openmsa.ps1 -VideoPath "D:\your\video\path.mp4"
```

If your credentials file is elsewhere:

```powershell
.\start-upload-openmsa.ps1 -CredentialsPath "D:\secrets\client_secret.json"
```

You can also keep the original command:

```powershell
.\upload-all.ps1 -VideoPath "D:\suraj2\Pictures\openmsa-intro.mp4" -CredentialsPath ".\client_secret.json"
```

Manual confirmation flow (click-style):

```powershell
.\upload-all.ps1 -VideoPath "D:\suraj2\Pictures\openmsa-intro.mp4" -CredentialsPath ".\client_secret.json" -Interactive
```

Before each channel upload, the script prints the current channel and the latest videos, then waits for Enter.

### Explicit profile overrides (optional)

```powershell
.\upload-all.ps1 `
  -VideoPath "D:\suraj2\Pictures\openmsa-intro.mp4" `
  -CredentialsPath ".\client_secret.json" `
  -ChromeBinary "C:\Program Files\Google\Chrome\Application\chrome.exe" `
  -ChromeUserDataDir "$env:LOCALAPPDATA\Google\Chrome\User Data" `
  -ChromeProfile "Default"
```

## Start button / pinning flow

Use the following for one-step desktop launch (interactive by default):

```powershell
.\start-upload-openmsa.cmd
```

- You can create a shortcut to `start-upload-openmsa.cmd` and pin it to Start for one-click usage.
- For quick updates, right-click the shortcut → Properties, set **Start in** to this folder.

Optional: generate shortcuts automatically (desktop + start menu):

```powershell
.\create-openmsa-upload-shortcut.ps1
```

Use `-DesktopOnly` or `-StartMenuOnly` if you want only one location.

## Useful checks

- If browser does not auto-open, open the printed OAuth URL manually in Chrome/Chromium.
- If OAuth shows redirect URI mismatch, verify the OAuth client includes:
  - `http://localhost:8787/oauth2callback`
- `upload.mjs` now validates before upload:
  - Authenticated channel name match (`expectedChannelName` in metadata)
  - latest uploads scan (`precheckUploadsLimit`)
  - duplicate title guard (`failOnDuplicateTitle`)
- Token files are created as:
  - `youtube-upload-kit/tokens/channel-1.token.json`
  - `youtube-upload-kit/tokens/channel-2.token.json`
- Use `-NoBrowser` if you need manual OAuth URL copy/paste.
