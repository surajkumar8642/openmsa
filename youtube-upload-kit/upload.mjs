import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { createReadStream, promises as fsPromises } from 'node:fs';
import { google } from 'googleapis';
import openBrowser from 'open';

const scopes = [
  'https://www.googleapis.com/auth/youtube.upload',
  'https://www.googleapis.com/auth/youtube.readonly'
];

function usage() {
  console.log(`
Usage:
  node upload.mjs --video <path> --metadata <path> --credentials <path>

Options:
  --video <path>         Full path to MP4
  --metadata <path>      Metadata json (channel-1.json / channel-2.json)
  --credentials <path>   OAuth client secret json from Google Cloud
  --chrome-profile <name> Chrome profile directory to use (default: Default)
  --chrome-binary <path> Full path to Chrome/Chromium executable
  --user-data-dir <path> Full path to Chrome/Chromium profile root (optional)
                         If omitted, this script auto-detects Chrome profile path.
  --no-browser           Skip opening browser automatically
`);
}

function fileExists(filePath) {
  return fs.existsSync(filePath);
}

function resolveChromeBinary(cliBinary) {
  if (cliBinary && fileExists(cliBinary)) return cliBinary;
  if (process.env.OPENMSA_CHROME_BINARY && fileExists(process.env.OPENMSA_CHROME_BINARY)) {
    return process.env.OPENMSA_CHROME_BINARY;
  }

  if (process.platform !== 'win32') {
    return null;
  }

  const localAppData = process.env.LOCALAPPDATA;
  const candidates = [
    path.join(process.env['PROGRAMFILES'] || 'C:\\Program Files', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env['PROGRAMFILES(X86)'] || 'C:\\Program Files (x86)', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    path.join(process.env.LOCALAPPDATA || '', 'Chromium', 'Application', 'chrome.exe')
  ];

  return candidates.find(fileExists) || null;
}

function resolveChromeUserDataDir(cliUserDataDir) {
  if (cliUserDataDir && fileExists(cliUserDataDir)) return cliUserDataDir;
  if (process.env.OPENMSA_CHROME_USER_DATA_DIR && fileExists(process.env.OPENMSA_CHROME_USER_DATA_DIR)) {
    return process.env.OPENMSA_CHROME_USER_DATA_DIR;
  }

  if (process.platform !== 'win32' || !process.env.LOCALAPPDATA) {
    return null;
  }

  const chromiumDataDir = path.join(process.env.LOCALAPPDATA, 'Chromium', 'User Data');
  const chromeDataDir = path.join(process.env.LOCALAPPDATA, 'Google', 'Chrome', 'User Data');

  if (fileExists(chromiumDataDir)) return chromiumDataDir;
  if (fileExists(chromeDataDir)) return chromeDataDir;
  return null;
}

function parseArgs(argv) {
  const args = {};
  for (let i = 2; i < argv.length; i++) {
    const key = argv[i];
    if (!key.startsWith('--')) continue;
    const value = argv[i + 1];
    if (key === '--no-browser') {
      args.noBrowser = true;
      continue;
    }
    if (!value || value.startsWith('--')) {
      continue;
    }
    args[key.slice(2)] = value;
    i++;
  }
  return args;
}

async function loadJson(filePath) {
  const raw = await fsPromises.readFile(filePath, 'utf8');
  return JSON.parse(raw);
}

function extractCredentials(credentials) {
  const source = credentials.installed ?? credentials.web;
  if (!source || !source.client_id || !source.client_secret) {
    throw new Error('Invalid Google credentials file format.');
  }

  const redirectUri =
    source.redirect_uris?.find((u) => u.startsWith('http://localhost')) ??
    'http://localhost:8787/oauth2callback';

  return {
    clientId: source.client_id,
    clientSecret: source.client_secret,
    redirectUri
  };
}

function waitForAuthCode(redirectUri) {
  const port = Number(new URL(redirectUri).port || '8787');
  const pathname = new URL(redirectUri).pathname;
  return new Promise((resolve, reject) => {
    const server = http.createServer((req, res) => {
      const url = new URL(req.url || '', `http://localhost:${port}`);
      if (url.pathname !== pathname) {
        res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
        res.end('Not found');
        return;
      }

      const code = url.searchParams.get('code');
      const error = url.searchParams.get('error');
      if (error) {
        res.writeHead(400, { 'content-type': 'text/plain; charset=utf-8' });
        res.end(`Authorization error: ${error}`);
        server.close();
        reject(new Error(`OAuth callback returned error: ${error}`));
        return;
      }
      if (!code) {
        res.writeHead(400, { 'content-type': 'text/plain; charset=utf-8' });
        res.end('Missing code');
        server.close();
        reject(new Error('OAuth callback missing code.'));
        return;
      }

      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      res.end('<h2>Authorization complete. You can close this tab.</h2>');
      server.close();
      resolve(code);
    });

    server.listen(port, () => {
      console.log(`OAuth callback listening on ${redirectUri}`);
    });
    server.on('error', (err) => {
      reject(err);
    });
  });
}

async function preUploadChecks(youtubeClient, metadata) {
  const channelResponse = await youtubeClient.channels.list({
    part: ['snippet', 'contentDetails'],
    mine: true
  });
  const channel = channelResponse.data.items?.[0];
  if (!channel) {
    throw new Error('No YouTube channel available for authenticated identity.');
  }

  const channelTitle = channel.snippet?.title || '<no-title>';
  const channelId = channel.id || '<no-id>';
  console.log(`Authenticated channel: ${channelTitle} (${channelId})`);

  if (metadata.expectedChannelName) {
    const expected = String(metadata.expectedChannelName).toLowerCase();
    if (!channelTitle.toLowerCase().includes(expected)) {
      throw new Error(`Expected channel name containing "${metadata.expectedChannelName}" but authenticated channel is "${channelTitle}".`);
    }
  }

  const scanLimit = Number(metadata.precheckUploadsLimit ?? 20);
  if (!Number.isFinite(scanLimit) || scanLimit < 0) {
    throw new Error('Invalid metadata.precheckUploadsLimit value; must be a non-negative number.');
  }

  const uploadsPlaylistId = channel.contentDetails?.relatedPlaylists?.uploads;
  const maxToLoad = Math.min(scanLimit, 200);
  const recent = [];
  let nextPageToken = undefined;
  while (recent.length < maxToLoad && uploadsPlaylistId) {
    const page = Math.min(50, maxToLoad - recent.length);
    const playlist = await youtubeClient.playlistItems.list({
      part: ['snippet'],
      playlistId: uploadsPlaylistId,
      maxResults: page,
      pageToken: nextPageToken
    });

    const items = playlist.data.items ?? [];
    for (const item of items) {
      if (!item?.snippet) continue;
      recent.push({
        title: item.snippet.title || '',
        id: item.snippet.resourceId?.videoId || '',
        publishedAt: item.snippet.publishedAt || ''
      });
      if (recent.length >= maxToLoad) break;
    }
    nextPageToken = playlist.data.nextPageToken;
    if (!nextPageToken) break;
  }

  if (recent.length > 0) {
    console.log('Recent uploads:');
    for (const item of recent) {
      const published = item.publishedAt ? new Date(item.publishedAt).toISOString() : 'unknown';
      console.log(`  - ${published} ${item.id || 'no-id'} | ${item.title}`);
    }
  } else {
    console.log('No recent uploads available for this channel.');
  }

  if (metadata.failOnDuplicateTitle && metadata.title) {
    const targetTitle = String(metadata.title).toLowerCase();
    const found = recent.some((item) => item.title.toLowerCase() === targetTitle);
    if (found) {
      throw new Error(`Duplicate title blocked by policy: "${metadata.title}". Use a different title or set failOnDuplicateTitle=false.`);
    }
  }
}

async function authorize(
  credentialsFile,
  tokenPath,
  videoMeta,
  noBrowser = false,
  chromeProfile = 'Default',
  browserBinary = null,
  userDataDir = null
) {
  const credentials = await loadJson(credentialsFile);
  const extracted = extractCredentials(credentials);
  const oAuth2Client = new google.auth.OAuth2(
    extracted.clientId,
    extracted.clientSecret,
    extracted.redirectUri
  );

  if (fs.existsSync(tokenPath)) {
    const token = JSON.parse(await fsPromises.readFile(tokenPath, 'utf8'));
    oAuth2Client.setCredentials(token);
    return oAuth2Client;
  }

  const url = oAuth2Client.generateAuthUrl({
    access_type: 'offline',
    scope: scopes,
    prompt: 'consent'
  });

  console.log(`Authorize upload for ${videoMeta.name || 'channel'}.`);
  console.log(`Open in browser: ${url}`);
  if (!noBrowser) {
    const browserArgs = [`--profile-directory=${chromeProfile}`];
    const finalUserDataDir = resolveChromeUserDataDir(userDataDir);
    if (finalUserDataDir) {
      browserArgs.push(`--user-data-dir=${finalUserDataDir}`);
    } else {
      console.log('No Chrome profile root detected. Opening with browser defaults.');
    }
    const browserPath = resolveChromeBinary(browserBinary);
    const appName = browserPath || process.env.OPENMSA_CHROME_BINARY || openBrowser.apps.chrome;
    if (!browserPath && process.env.OPENMSA_CHROME_BINARY) {
      console.log(`OPENMSA_CHROME_BINARY is set but not found at: ${process.env.OPENMSA_CHROME_BINARY}`);
    }
    try {
      await openBrowser(url, {
        app: {
          name: appName,
          arguments: browserArgs
        }
      });
      const target = appName || 'default browser launcher';
      console.log(`Opened browser for OAuth at ${target}`);
      if (browserArgs.length) {
        console.log(`Browser args: ${browserArgs.join(' ')}`);
      }
    } catch (error) {
      console.log('Auto-open failed, open the URL manually.');
    }
  }

  const code = await waitForAuthCode(extracted.redirectUri);
  const { tokens } = await oAuth2Client.getToken(code);
  oAuth2Client.setCredentials(tokens);

  const tokenDir = path.dirname(tokenPath);
  if (!fs.existsSync(tokenDir)) fs.mkdirSync(tokenDir, { recursive: true });
  await fsPromises.writeFile(tokenPath, JSON.stringify(tokens, null, 2), 'utf8');
  console.log(`Saved token -> ${tokenPath}`);
  return oAuth2Client;
}

async function uploadVideo(videoPath, metadata, oAuth2Client) {
  const youtube = google.youtube({ version: 'v3', auth: oAuth2Client });
  const file = createReadStream(videoPath);
  const title = metadata.title || path.basename(videoPath);
  const description = metadata.description || '';
  const tags = Array.isArray(metadata.tags) ? metadata.tags : [];

  console.log(`Uploading: ${path.basename(videoPath)}`);
  const uploadResponse = await youtube.videos.insert(
    {
      part: ['snippet', 'status'],
      requestBody: {
        snippet: {
          title,
          description,
          tags,
          categoryId: metadata.categoryId || '28'
        },
        status: {
          privacyStatus: metadata.privacyStatus || 'public',
          selfDeclaredMadeForKids: false
        }
      },
      media: {
        body: file
      }
    },
    {
      onUploadProgress: (evt) => {
        const size = Number(evt.bytesWritten || 0);
        if (size > 0) {
          process.stdout.write(`\rUploaded: ${Math.round(size / (1024 * 1024))} MB`);
        }
      }
    }
  );
  console.log();

  const videoId = uploadResponse.data?.id;
  if (!videoId) throw new Error('Upload response missing video id.');
  console.log(`Upload complete: https://www.youtube.com/watch?v=${videoId}`);

  if (metadata.thumbnail && fs.existsSync(metadata.thumbnail)) {
    await youtube.thumbnails.set({
      videoId,
      media: {
        body: createReadStream(metadata.thumbnail)
      }
    });
    console.log(`Thumbnail set: ${metadata.thumbnail}`);
  }

  if (metadata.playlistId) {
    await youtube.playlistItems.insert({
      part: ['snippet'],
      requestBody: {
        snippet: {
          playlistId: metadata.playlistId,
          resourceId: {
            kind: 'youtube#video',
            videoId
          }
        }
      }
    });
    console.log(`Added to playlist: ${metadata.playlistId}`);
  }
}

async function main() {
  const args = parseArgs(process.argv);
  if (!args.video || !args.metadata || !args.credentials) {
    usage();
    process.exit(1);
  }

  const metadata = await loadJson(path.resolve(args.metadata));
  const credentialsPath = path.resolve(args.credentials);
  const videoPath = path.resolve(args.video);

  if (!fs.existsSync(videoPath)) {
    throw new Error(`Video not found: ${videoPath}`);
  }
  if (!fs.existsSync(credentialsPath)) {
    throw new Error(`Credentials not found: ${credentialsPath}`);
  }

  const tokenPath = path.resolve(metadata.tokenPath || `${metadata.name || 'channel'}.token.json`);
  const auth = await authorize(
    credentialsPath,
    tokenPath,
    metadata,
    Boolean(args['no-browser']),
    args['chrome-profile'] || 'Default',
    args['chrome-binary'] || null,
    args['user-data-dir'] || null
  );
  const youtube = google.youtube({ version: 'v3', auth });
  await preUploadChecks(youtube, metadata);

  await uploadVideo(videoPath, metadata, auth);
}

main().catch((error) => {
  console.error(error?.message || error);
  process.exit(1);
});
