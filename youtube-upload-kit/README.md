# OpenMSA YouTube Upload Kit

Upload `openmsa-intro.mp4` to two YouTube channels from the same file.

## What this kit includes

- `upload.mjs` — Node uploader using YouTube Data API v3
- `channel-1.json` and `channel-2.json` — per-channel metadata
- `upload-all.ps1` — one-command upload to both channels
- `materials-checklist.md` — upload material checklist
- `tokens/` — auto-created tokens after first authorization (not committed)
- `assets/` — place thumbnails here (optional)

## Why your credentials are needed

I can’t access your YouTube login from this environment, so OAuth login must be completed manually in your browser once per channel (token is then reused locally).

The scripts can open Chrome/Chromium using your permanent profile:
- Chrome profile directory (usually `Default`) via `--chrome-profile`
- Optional browser executable path via `--chrome-binary`
- Optional profile root via `--user-data-dir`

## Setup

From `openmsa/youtube-upload-kit`:

```powershell
npm install
```

1. Create OAuth credentials in Google Cloud:
   - API: **YouTube Data API v3**
   - OAuth client type: **Desktop app** (recommended) or **Web app**
   - Add redirect URI: `http://localhost:8787/oauth2callback`
2. Save credentials file as `client_secret.json` in this folder.
3. Update metadata in `channel-1.json` / `channel-2.json`.
4. (Optional) configure permanent browser path/user-data in PowerShell:

```powershell
$env:OPENMSA_CHROME_BINARY = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$env:OPENMSA_CHROME_USER_DATA_DIR = "$env:LOCALAPPDATA\Google\Chrome\User Data"
```

## Upload now (same file to both channels)

```powershell
.\upload-all.ps1 -VideoPath "D:\suraj2\Pictures\openmsa-intro.mp4" -CredentialsPath ".\client_secret.json"
```

You can force profile details for Chromium with explicit args:

```powershell
.\upload-all.ps1 `
  -VideoPath "D:\suraj2\Pictures\openmsa-intro.mp4" `
  -CredentialsPath ".\client_secret.json" `
  -ChromeBinary "C:\Program Files\Chromium\Application\chrome.exe" `
  -ChromeUserDataDir "C:\Users\<your-user>\AppData\Local\Chromium\User Data" `
  -ChromeProfile "Default"
```

## Useful checks

- If browser does not auto-open, open the printed OAuth URL manually in Chrome/Chromium.
- If you see redirect URI mismatch, verify your OAuth client includes:
  - `http://localhost:8787/oauth2callback`
- Tokens are saved after first login as:
  - `youtube-upload-kit/tokens/channel-1.token.json`
  - `youtube-upload-kit/tokens/channel-2.token.json`
- If browser auto-open does not happen, use `-NoBrowser` and copy-paste the printed URL manually.
