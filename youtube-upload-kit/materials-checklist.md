# OpenMSA intro upload materials checklist

Source video: `D:\suraj2\Pictures\openmsa-intro.mp4`

Per channel you need:
- MP4 source
- Optional thumbnail (JPG/PNG, recommended 1280x720)
- Final title + description + tags + visibility
- Optional: playlist ID

Prepared files:
- `upload.mjs` (uploader)
- `upload-all.ps1` (dual-channel runner)
- `channel-1.json`, `channel-2.json` (metadata)
- `.env` is not required; use direct PowerShell env vars or CLI arguments
- `assets/` (optional thumbnails)

After first successful authorization:
- `tokens/channel-1.token.json` created
- `tokens/channel-2.token.json` created

Do not share token or credential files.
