# YouTube Downloader Firefox Extension

A Firefox extension that integrates with YoutubeDownloader.Core to download YouTube videos directly from your browser.

## Components

1. **Firefox Extension** (`YTDownloadExtension/`) - Browser extension with popup UI
2. **Download Server** (`YTDownloadServer/`) - Local .NET server that handles downloads

## Installation & Setup

### Prerequisites
- Firefox browser
- .NET 9.0 SDK installed
- FFmpeg (will be downloaded automatically or use system installation)

### Step 1: Start the Download Server

```bash
cd YTDownloadServer
dotnet run
```

The server will start on `http://localhost:5000`

### Step 2: Load the Firefox Extension

1. Open Firefox and navigate to `about:debugging`
2. Click "This Firefox" on the left sidebar
3. Click "Load Temporary Add-on"
4. Navigate to `YTDownloadExtension/` folder
5. Select any file (e.g., `manifest.json`)
6. The extension will appear in your Firefox toolbar with a red "YT" icon

## Usage

1. Navigate to any YouTube video page
2. Click the YT extension icon in the toolbar
3. Select quality (1080p or 720p MP4)
4. Click "Download Video"
5. The video will be downloaded to your Downloads folder

## Features

- Automatic detection of YouTube video pages
- 1080p MP4 preferred, falls back to 720p if unavailable
- Downloads saved to user's Downloads folder with video title as filename
- Duplicate file handling (appends number if file exists)
- Server health check before download attempts

## Architecture

### Extension Components
- **manifest.json**: Extension configuration
- **background.js**: Handles messaging and download requests
- **content.js**: Detects YouTube videos and extracts information
- **popup.html/js/css**: User interface for download control

### Server Components
- **DownloadController**: REST API endpoints for download requests
- **DownloadService**: Wraps YoutubeDownloader.Core functionality
- **Program.cs**: Configures CORS and server settings

### API Endpoints
- `GET /api/health` - Server health check
- `POST /api/download` - Initiate video download
  ```json
  {
    "videoId": "video_id_here",
    "videoTitle": "optional_title",
    "quality": "1080p"
  }
  ```

## Troubleshooting

### Server won't start
- Ensure .NET 9.0 SDK is installed: `dotnet --version`
- Check if port 5000 is available
- Run with: `dotnet run --urls http://localhost:5000`

### Extension can't connect to server
- Verify server is running on localhost:5000
- Check browser console for CORS errors
- Ensure no firewall blocking localhost connections

### Downloads fail
- Check server console for error messages
- Verify FFmpeg is available (in YTDownloadServer folder or system PATH)
- Ensure Downloads folder is accessible and writable

## Development

### Building the Server
```bash
cd YTDownloadServer
dotnet build
```

### Modifying the Extension
After making changes to extension files:
1. Go to `about:debugging`
2. Click "Reload" on the extension

### Server Logs
The server will output logs to console showing:
- Incoming requests
- Download progress
- Any errors encountered

## Notes

- This is a development setup using temporary extension loading
- For production use, the extension should be signed and published
- The server must be running for downloads to work
- Downloads use YoutubeExplode library which may break if YouTube changes their API