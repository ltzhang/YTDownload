# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

YoutubeDownloader is a cross-platform desktop application for downloading videos from YouTube, built with:
- **.NET 9.0** and C# (with preview language features enabled)
- **Avalonia UI** framework for cross-platform GUI
- **YoutubeExplode** library for YouTube interaction
- **FFmpeg** for video processing (bundled or external)

## Architecture

The solution consists of two main projects:

### YoutubeDownloader.Core
- Contains core downloading logic, video resolution, tagging, and query processing
- Key components:
  - `VideoDownloader`: Main download orchestrator using YoutubeExplode.Converter
  - `QueryResolver`: Handles URL/query parsing and resolution
  - `FFmpeg`: FFmpeg process management and operations
  - `MediaTagInjector`: Injects metadata tags into downloaded media files

### YoutubeDownloader (Main Application)
- Avalonia-based desktop application using MVVM pattern
- Key patterns:
  - ViewModels in `ViewModels/` directory (inherit from `ViewModelBase`)
  - Views in `Views/` directory (`.axaml` and `.axaml.cs` files)
  - Dialogs managed through `DialogManager` service
  - Settings persistence via `SettingsService` using Cogwheel library
  - Dependency injection with Microsoft.Extensions.DependencyInjection

## Development Commands

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run --project YoutubeDownloader/YoutubeDownloader.csproj
```

### Format Check
```bash
dotnet build -t:CSharpierFormat --configuration Release
```

### Publish (with runtime-specific FFmpeg)
```bash
dotnet publish -c Release -r <runtime-identifier>
```
Common runtime identifiers: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`

### Download FFmpeg (PowerShell)
```powershell
pwsh YoutubeDownloader/Download-FFmpeg.ps1
```

## Important Conventions

- **Code formatting**: Project uses CSharpier auto-formatter (runs on build)
- **Nullable reference types**: Enabled project-wide
- **Warnings as errors**: All warnings are treated as errors
- **No code comments**: Follow existing pattern of self-documenting code
- **MVVM pattern**: ViewModels use CommunityToolkit.Mvvm for property change notifications
- **Async patterns**: Heavy use of async/await for YouTube operations and downloads
- **Dialog system**: Use `DialogManager` to show dialogs, not direct window manipulation

## FFmpeg Handling

The application can work with FFmpeg in two ways:
1. **Bundled**: FFmpeg executable in the project directory (automatically downloaded during build if `DownloadFFmpeg=true`)
2. **System**: Uses FFmpeg from system PATH if no local copy exists

For development, place `ffmpeg.exe` (Windows) or `ffmpeg` (Linux/macOS) in the `YoutubeDownloader/` directory, or run the PowerShell script to download it automatically.

## Testing Considerations

- No automated test suite currently exists
- Manual testing required for download functionality
- Test with various video URLs, playlists, and channels
- Verify FFmpeg operations and media tagging