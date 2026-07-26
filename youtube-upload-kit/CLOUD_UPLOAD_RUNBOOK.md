# OpenMSA YouTube Upload – Cloud/Manual Runbook

Use this when you want manual, click-by-click uploading using your already logged-in Chromium profile.

## 0) What this runbook is for
- Upload `openmsa-intro-final-voice.mp4` to your EC channel in two places (channel-1 and channel-2 metadata).
- Keep each upload manual in the browser (click workflow), while the metadata and checks stay controlled.
- Use this on the same Windows login where Chromium profile is already authenticated.

## 1) Launch Chromium with your existing profile
Open PowerShell and run:

```powershell
$chrome = "C:\Users\suraj2\AppData\Local\Chromium\Application\chrome.exe"
$userData = "C:\Users\suraj2\AppData\Local\Chromium\User Data"
$profile = "Default"
$url = "https://studio.youtube.com/channel/UCQ42FdzfJ8OAgtInyvYIsZA/videos/upload?d=ud&filter=%5B%5D&sort=%7B%22columnType%22%3A%22date%22%2C%22sortOrder%22%3A%22DESCENDING%22%7D"
$args = @("--user-data-dir=$userData", "--profile-directory=$profile", "--new-window", $url)
Start-Process -FilePath $chrome -ArgumentList $args
```

## 2) Channel selection (manual)
You should always confirm the visible channel name is `EC` before continuing.

If you have multiple channels:
1. Click your profile icon.
2. Switch to the EC channel.
3. Return to the upload page URL above.

## 3) Prepare metadata before upload
Open metadata files and keep values ready:
- `youtube-upload-kit/channel-1.json`
- `youtube-upload-kit/channel-2.json`

At minimum, use:
- Title
- Description
- Tags
- Visibility (privacy)

## 4) Manual upload flow (recommended)
For each target metadata entry:
1. Click **Create** → **Upload videos** in YouTube Studio.
2. Drag or choose `openmsa-intro-final-voice.mp4`.
3. Set Title/Description exactly as intended.
4. Set tags if needed.
5. Set privacy status (usually Public/Unlisted for this project).
6. Click **Next** for all editor steps.
7. On final step, click **Publish**.
8. Copy the published video URL and save it.

## 5) Duplicate / wrong-channel guard
Before each upload:
- Confirm channel title is EC.
- Verify upload list (sorted by date desc) does not already contain the same title.
- If title already exists, change title before publishing.

## 6) Post-upload verification
After each publish:
1. Open the uploaded video's URL in a new tab.
2. Confirm playback and check description/title.
3. Update your local notes/log:
   - Channel
   - Title
   - Video ID
   - Timestamp

## 7) Known quick commands for local script mode
If you decide to switch back to script-assisted uploads:
- Interactive one-click launcher:
  ```powershell
  cd "C:\Work\suraj\github\MSA\openmsa\youtube-upload-kit"
  .\start-upload-openmsa.ps1 -Interactive
  ```
- Explicit Chromium profile:
  ```powershell
  .\start-upload-openmsa.ps1 -Interactive -ChromeBinary "C:\Users\suraj2\AppData\Local\Chromium\Application\chrome.exe" -ChromeUserDataDir "C:\Users\suraj2\AppData\Local\Chromium\User Data" -ChromeProfile "Default"
  ```

## 8) Troubleshooting
- If Chromium opens a wrong page/account: close all Chromium windows and reopen with the command above.
- If upload page is blocked or unavailable, check Google account sign-in first, then retry.
- If upload fails mid-way, retry using the same video file with a slightly changed title.

## 9) Important
This runbook is intentionally manual-first.
Automation can prepare checks, but final upload confirmation remains a click-based step by you.
