# YTDownloadCli - YouTube Downloader CLI Tool

A command-line tool for downloading YouTube videos, similar to `wget` but specifically for YouTube content. Features automatic retries with exponential backoff and progress tracking.

## Features

- **Simple wget-like interface** - Just provide a URL to download
- **Batch downloads** - Download multiple videos from a text file
- **Smart quality fallback** - Defaults to 1080p, automatically falls back to best available
- **Automatic retries** - Progressive backoff (1s, 5s, 10s, 30s, 60s)
- **Progress bar** - Visual download progress indicator
- **Quality selection** - Choose from 360p to 1080p or highest available
- **Audio-only downloads** - Extract audio as MP3
- **Quiet mode** - Suppress output for scripting

## Installation

### Build from source
```bash
cd YTDownloadCli
dotnet build
```

### Install as global tool
```bash
cd YTDownloadCli
dotnet pack
dotnet tool install --global --add-source ./nupkg YTDownloadCli
```

After installation, the `ytd` command will be available globally.

## Usage

### Basic download
```bash
ytd https://www.youtube.com/watch?v=VIDEO_ID
```

### Specify output filename
```bash
ytd https://www.youtube.com/watch?v=VIDEO_ID -o "my_video.mp4"
```

### Download in specific quality
```bash
ytd https://www.youtube.com/watch?v=VIDEO_ID -q 720p
```

### Download audio only
```bash
ytd https://www.youtube.com/watch?v=VIDEO_ID -a -o "audio.mp3"
```

### Quiet mode (no progress bar)
```bash
ytd https://www.youtube.com/watch?v=VIDEO_ID -s
```

### Custom retry count
```bash
ytd https://www.youtube.com/watch?v=VIDEO_ID -r 10
```

## Options

- `<url>` - YouTube video URL (optional when using -f)
- `-o, --output` - Output file path (default: video title)
- `-f, --file` - Text file containing URLs (one per line)
- `-d, --output-dir` - Output directory for batch downloads (default: current directory)
- `-q, --quality` - Video quality: 360p, 480p, 720p, 1080p, highest (default: 1080p, auto-fallback to best available)
- `-r, --retries` - Number of retry attempts (default: 5)
- `-s, --quiet` - Suppress progress output
- `-a, --audio-only` - Download audio only as MP3
- `-h, --help` - Show help

## Examples

### Download with automatic filename
```bash
ytd https://www.youtube.com/watch?v=dQw4w9WgXcQ
# Downloads as "Rick Astley - Never Gonna Give You Up.mp4"
```

### Batch download from file
```bash
# Create a file with URLs
echo "https://www.youtube.com/watch?v=VIDEO_ID1" > urls.txt
echo "https://www.youtube.com/watch?v=VIDEO_ID2" >> urls.txt

# Download all videos from the file
ytd -f urls.txt -d downloads/

# Download as audio files
ytd -f urls.txt -d music/ -a
```

### Download best quality with custom name
```bash
ytd https://www.youtube.com/watch?v=dQw4w9WgXcQ -q highest -o rickroll.mp4
```

### Extract audio for music
```bash
ytd https://www.youtube.com/watch?v=dQw4w9WgXcQ -a -o "never_gonna_give_you_up.mp3"
```

### Script-friendly quiet mode
```bash
ytd https://www.youtube.com/watch?v=dQw4w9WgXcQ -s -o output.mp4 && echo "Download complete"

# Batch download in quiet mode
ytd -f urls.txt -s -d output/
```

## Batch Download File Format

The input file for batch downloads supports:
- One URL per line
- Lines starting with `#` are treated as comments and ignored
- Empty lines are skipped
- 2-second delay between downloads to avoid rate limiting

Example `urls.txt`:
```
# My favorite videos
https://www.youtube.com/watch?v=VIDEO_ID1
https://www.youtube.com/watch?v=VIDEO_ID2

# Music playlist
https://www.youtube.com/watch?v=VIDEO_ID3
https://www.youtube.com/watch?v=VIDEO_ID4
```

## Quality Selection Behavior

The tool intelligently handles video quality:
- **Default**: Always tries 1080p MP4 first
- **Fallback**: If requested quality isn't available, automatically downloads the best available quality up to the requested level
- **User preference**: When you specify a quality with `-q`, it respects your choice but still falls back if needed
- **Notification**: Informs you when downloading a lower quality than requested (e.g., "Note: 1080p not available, downloading 720p instead")

Examples:
- Video only has 720p → Downloads 720p even if 1080p was requested
- Video has 4K → Downloads 1080p by default (unless `-q highest` is used)
- `-q 480p` → Downloads best quality up to 480p

## Retry Behavior

When downloads fail, the tool automatically retries with progressive delays:
- 1st retry: 1 second
- 2nd retry: 5 seconds
- 3rd retry: 10 seconds
- 4th retry: 30 seconds
- 5th retry: 60 seconds

## Exit Codes

- `0` - Success
- `1` - Failure (invalid URL, download failed after retries, etc.)

## Requirements

- .NET 9.0 Runtime
- FFmpeg (must be in PATH or in application directory)

## Troubleshooting

### FFmpeg not found
Ensure FFmpeg is installed and available in your system PATH, or place the FFmpeg executable in the same directory as the CLI tool.

### Download fails repeatedly
YouTube may have rate limits or the video may be restricted. Try:
- Waiting a few minutes before retrying
- Using fewer retries to avoid rate limiting
- Checking if the video is available in your region