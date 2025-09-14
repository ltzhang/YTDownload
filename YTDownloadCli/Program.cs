using System.CommandLine;
using System.Net;
using System.Threading;
using YoutubeDownloader.Core.Downloading;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Common;
using Gress;
using YTDownloadCli;

var urlArgument = new Argument<string>("url", "YouTube video URL to download")
{
    Arity = ArgumentArity.ZeroOrOne
};

var outputOption = new Option<string?>(
    aliases: new[] { "--output", "-o" },
    description: "Output file path (default: video title)",
    getDefaultValue: () => null
);

var fileOption = new Option<string?>(
    aliases: new[] { "--file", "-f" },
    description: "Text file containing URLs (one per line)",
    getDefaultValue: () => null
);

var outputDirOption = new Option<string?>(
    aliases: new[] { "--output-dir", "-d" },
    description: "Output directory for batch downloads",
    getDefaultValue: () => "."
);

var qualityOption = new Option<string>(
    aliases: new[] { "--quality", "-q" },
    description: "Video quality (360p, 480p, 720p, 1080p, highest)",
    getDefaultValue: () => "1080p"
);

var retriesOption = new Option<int>(
    aliases: new[] { "--retries", "-r" },
    description: "Number of retry attempts",
    getDefaultValue: () => 5
);

var quietOption = new Option<bool>(
    aliases: new[] { "--quiet", "-s" },
    description: "Suppress progress output",
    getDefaultValue: () => false
);

var audioOnlyOption = new Option<bool>(
    aliases: new[] { "--audio-only", "-a" },
    description: "Download audio only (MP3)",
    getDefaultValue: () => false
);

var rootCommand = new RootCommand("YouTube downloader CLI tool - works like wget for YouTube videos")
{
    urlArgument,
    outputOption,
    fileOption,
    outputDirOption,
    qualityOption,
    retriesOption,
    quietOption,
    audioOnlyOption
};

rootCommand.SetHandler(async (context) =>
{
    var url = context.ParseResult.GetValueForArgument(urlArgument);
    var output = context.ParseResult.GetValueForOption(outputOption);
    var file = context.ParseResult.GetValueForOption(fileOption);
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;
    var quality = context.ParseResult.GetValueForOption(qualityOption)!;
    var retries = context.ParseResult.GetValueForOption(retriesOption);
    var quiet = context.ParseResult.GetValueForOption(quietOption);
    var audioOnly = context.ParseResult.GetValueForOption(audioOnlyOption);

    // Set up Ctrl+C handler for immediate exit
    using var cts = new CancellationTokenSource();
    var exitRequested = false;
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        if (!exitRequested)
        {
            exitRequested = true;
            cts.Cancel();
            if (!quiet)
                Console.WriteLine("\n\nDownload cancelled by user. Preserving partial downloads...");
        }
    };

    var cancellationToken = cts.Token;

    // Validate arguments
    if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(file))
    {
        Console.Error.WriteLine("Error: Either provide a URL or use -f to specify a file with URLs");
        Environment.Exit(1);
    }

    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(file))
    {
        Console.Error.WriteLine("Error: Cannot use both URL and file input at the same time");
        Environment.Exit(1);
    }

    var downloader = new YtdDownloader(quiet);
    bool result = true;

    // Single URL download
    if (!string.IsNullOrEmpty(url))
    {
        result = await downloader.DownloadWithRetryAsync(
            url,
            output,
            quality,
            audioOnly,
            retries,
            cancellationToken
        );
    }
    // Batch download from file
    else if (!string.IsNullOrEmpty(file))
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: File not found: {file}");
            Environment.Exit(1);
        }

        result = await downloader.DownloadFromFileAsync(
            file,
            outputDir,
            quality,
            audioOnly,
            retries,
            cancellationToken
        );
    }

    if (cancellationToken.IsCancellationRequested)
    {
        Environment.Exit(130); // Standard exit code for SIGINT
    }
    else
    {
        Environment.Exit(result ? 0 : 1);
    }
});

return await rootCommand.InvokeAsync(args);

public class YtdDownloader
{
    private readonly bool _quiet;
    private readonly VideoDownloader _downloader;
    private readonly YoutubeClient _youtube;
    private readonly int[] _retryDelays = { 1, 5, 10, 30, 60 };

    public YtdDownloader(bool quiet)
    {
        _quiet = quiet;
        _downloader = new VideoDownloader();
        _youtube = new YoutubeClient();
    }

    public async Task<bool> DownloadFromFileAsync(
        string filePath,
        string outputDir,
        string quality,
        bool audioOnly,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var urls = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var validUrls = urls
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Select(line => line.Trim())
            .ToList();

        if (validUrls.Count == 0)
        {
            Console.Error.WriteLine("Error: No valid URLs found in file");
            return false;
        }

        if (!_quiet)
        {
            Console.WriteLine($"Found {validUrls.Count} URLs to download");
            Console.WriteLine(new string('-', 50));
        }

        int successful = 0;
        int failed = 0;

        for (int i = 0; i < validUrls.Count; i++)
        {
            var url = validUrls[i];

            if (!_quiet)
            {
                Console.WriteLine($"\n[{i + 1}/{validUrls.Count}] Processing: {url}");
            }

            try
            {
                // Generate output path in the specified directory
                var outputPath = Path.Combine(outputDir, GetFileNameFromUrl(url, audioOnly));

                var success = await DownloadWithRetryAsync(
                    url,
                    outputPath,
                    quality,
                    audioOnly,
                    maxRetries,
                    cancellationToken
                );

                if (success)
                {
                    successful++;
                    if (!_quiet)
                        Console.WriteLine($"✓ Successfully downloaded [{successful}/{i + 1}]");
                }
                else
                {
                    failed++;
                    if (!_quiet)
                        Console.WriteLine($"✗ Failed to download [{failed} failed so far]");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"Error processing URL: {ex.Message}");
            }

            // Add a small delay between downloads to avoid rate limiting
            if (i < validUrls.Count - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        if (!_quiet)
        {
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"Download Summary:");
            Console.WriteLine($"  Successful: {successful}/{validUrls.Count}");
            Console.WriteLine($"  Failed: {failed}/{validUrls.Count}");
            Console.WriteLine(new string('=', 50));
        }

        return failed == 0;
    }

    private string GetFileNameFromUrl(string url, bool audioOnly)
    {
        // Try to extract video ID for temporary filename
        var videoId = VideoId.TryParse(url);
        if (videoId != null)
        {
            return $"{videoId.Value}{(audioOnly ? ".mp3" : ".mp4")}";
        }

        // Fallback to timestamp-based name
        return $"video_{DateTime.Now:yyyyMMdd_HHmmss}{(audioOnly ? ".mp3" : ".mp4")}";
    }

    public async Task<bool> DownloadWithRetryAsync(
        string url,
        string? outputPath,
        string quality,
        bool audioOnly,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var videoId = VideoId.TryParse(url);
        if (videoId == null)
        {
            Console.Error.WriteLine($"Error: Invalid YouTube URL: {url}");
            return false;
        }

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = attempt <= _retryDelays.Length
                    ? _retryDelays[attempt - 1]
                    : _retryDelays[^1];

                if (!_quiet)
                    Console.WriteLine($"Retry {attempt}/{maxRetries} in {delay} seconds...");

                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
            }

            try
            {
                return await DownloadWithStallDetectionAsync(videoId.Value, outputPath, quality, audioOnly, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Download cancelled or stalled.");

                // On stall, force retry
                if (attempt < maxRetries)
                {
                    Console.WriteLine("Retrying due to stall...");
                    continue;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");

                if (attempt == maxRetries)
                {
                    Console.Error.WriteLine($"Failed after {maxRetries} retries.");
                    return false;
                }
            }
        }

        return false;
    }

    private async Task<bool> DownloadWithStallDetectionAsync(
        VideoId videoId,
        string? outputPath,
        string quality,
        bool audioOnly,
        CancellationToken cancellationToken)
    {
        var video = await _youtube.Videos.GetAsync(videoId, cancellationToken);

        var fileName = outputPath ?? SanitizeFileName(video.Title);
        if (!Path.HasExtension(fileName))
        {
            fileName += audioOnly ? ".mp3" : ".mp4";
        }

        // If outputPath was provided and it's a directory, use video title as filename
        if (outputPath != null && Directory.Exists(outputPath))
        {
            fileName = Path.Combine(outputPath, SanitizeFileName(video.Title) + (audioOnly ? ".mp3" : ".mp4"));
        }

        var finalPath = Path.GetFullPath(fileName);

        // Create download tracker
        var tracker = new DownloadTracker(videoId.Value, finalPath);

        // Get fresh stream info for accurate size
        var container = audioOnly ? Container.Mp3 : Container.Mp4;
        var preference = GetQualityPreference(quality);
        var downloadOption = await _downloader.GetBestDownloadOptionAsync(
            videoId,
            new VideoDownloadPreference(container, preference),
            includeLanguageSpecificAudioStreams: false,
            cancellationToken
        );

        // Get actual file size from streams
        long estimatedSize = 0;
        var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);

        // Get the actual streams that will be downloaded
        var videoStream = downloadOption.StreamInfos.OfType<IVideoStreamInfo>().FirstOrDefault();
        var audioStream = downloadOption.StreamInfos.OfType<IAudioStreamInfo>().FirstOrDefault();

        if (videoStream != null)
            estimatedSize += videoStream.Size.Bytes;
        if (audioStream != null && audioStream != videoStream) // Don't count twice if muxed
            estimatedSize += audioStream.Size.Bytes;

        tracker.TotalBytes = estimatedSize;

        // Check if we can resume
        if (tracker.CanResume())
        {
            if (!_quiet)
            {
                var existingBytes = tracker.DownloadedBytes;
                var remainingBytes = estimatedSize - existingBytes;
                var percentage = estimatedSize > 0 ? (int)((double)existingBytes / estimatedSize * 100) : 0;

                Console.WriteLine($"\n=== RESUME INFORMATION ===");
                Console.WriteLine($"Total file size: {FormatFileSize(estimatedSize)}");
                Console.WriteLine($"Already downloaded: {FormatFileSize(existingBytes)} ({percentage}%)");
                Console.WriteLine($"Remaining to download: {FormatFileSize(remainingBytes)}");
                Console.WriteLine($"Resuming partial download...\n");
            }
        }
        else if (File.Exists(finalPath))
        {
            // Check if file already exists and is complete
            var existingFile = new FileInfo(finalPath);
            if (existingFile.Length > 0)
            {
                if (!_quiet)
                {
                    Console.WriteLine($"File already exists: {finalPath}");
                    Console.Write("Overwrite? (y/N): ");
                    var response = Console.ReadLine();
                    if (response?.ToLower() != "y")
                    {
                        Console.WriteLine("Skipping download.");
                        return true;
                    }
                }
                File.Delete(finalPath);
                tracker.Cleanup();
            }
        }

        // Create cancellation token that triggers on stall
        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var result = await DownloadWithTrackerAsync(video, tracker, downloadOption, audioOnly, stallCts);

            // Only clean up metadata on successful completion
            if (result)
            {
                tracker.Cleanup();
            }

            return result;
        }
        catch (OperationCanceledException) when (stallCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // This was a stall, not a user cancellation
            throw new OperationCanceledException("Download stalled");
        }
        catch (OperationCanceledException)
        {
            // User cancellation (Ctrl+C) - preserve partial download for resume
            if (!_quiet)
            {
                Console.WriteLine("Download interrupted. Partial file preserved for resume.");
            }
            tracker.SaveMetadata(); // Ensure metadata is saved
            throw;
        }
    }

    private async Task<bool> DownloadWithTrackerAsync(
        IVideo video,
        DownloadTracker tracker,
        VideoDownloadOption downloadOption,
        bool audioOnly,
        CancellationTokenSource stallCts)
    {
        if (!_quiet)
        {
            Console.WriteLine($"Title: {video.Title}");
            Console.WriteLine($"Author: {video.Author}");
            Console.WriteLine($"Duration: {video.Duration}");
        }

        var directory = Path.GetDirectoryName(tracker.OutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Store download URL and size for resume capability
        tracker.DownloadUrl = video.Url;

        if (!_quiet)
        {
            Console.WriteLine($"Quality: {downloadOption.VideoQuality?.Label ?? "Audio Only"}");
            Console.WriteLine($"Estimated size: {FormatFileSize(tracker.TotalBytes)}");
            Console.WriteLine($"Output: {tracker.OutputPath}");
            Console.WriteLine("Downloading...");
        }

        var progressBar = _quiet ? null : new ConsoleProgressBar(tracker.TotalBytes);

        // Create stall-detecting progress
        using var stallProgress = new StallDetectingProgress(
            tracker,
            progressBar != null ? new Progress<Percentage>(p => progressBar.Update(p.Value)) : null,
            (message) =>
            {
                if (!_quiet)
                    Console.WriteLine($"\n{message}");
                stallCts.Cancel();
            }
        );

        await _downloader.DownloadVideoAsync(
            tracker.OutputPath,
            video,
            downloadOption,
            includeSubtitles: false,
            progress: stallProgress,
            cancellationToken: stallCts.Token
        );

        progressBar?.Complete();

        if (!_quiet)
        {
            var fileInfo = new FileInfo(tracker.OutputPath);
            Console.WriteLine($"\nDownloaded successfully: {FormatFileSize(fileInfo.Length)}");
        }

        return true;
    }

    private async Task<bool> DownloadAsync(
        VideoId videoId,
        string? outputPath,
        string quality,
        bool audioOnly,
        CancellationToken cancellationToken)
    {
        if (!_quiet)
            Console.WriteLine($"Fetching video information...");

        var video = await _youtube.Videos.GetAsync(videoId, cancellationToken);

        if (!_quiet)
        {
            Console.WriteLine($"Title: {video.Title}");
            Console.WriteLine($"Author: {video.Author}");
            Console.WriteLine($"Duration: {video.Duration}");
        }

        var container = audioOnly ? Container.Mp3 : Container.Mp4;
        var preference = GetQualityPreference(quality);

        // Get the best available option up to the preferred quality
        // The VideoDownloadPreference already handles fallback to lower qualities
        var downloadOption = await _downloader.GetBestDownloadOptionAsync(
            videoId,
            new VideoDownloadPreference(container, preference),
            includeLanguageSpecificAudioStreams: false,
            cancellationToken
        );

        // If we get a lower quality than requested, inform the user
        if (!_quiet && !audioOnly && downloadOption.VideoQuality != null)
        {
            var actualQuality = downloadOption.VideoQuality.Value;
            var actualHeight = int.Parse(actualQuality.Label.Replace("p", "").Split(' ')[0]);

            if (quality == "1080p" && actualHeight < 1080)
            {
                Console.WriteLine($"Note: 1080p not available, downloading {actualQuality.Label} instead");
            }
            else if (quality == "720p" && actualHeight < 720)
            {
                Console.WriteLine($"Note: 720p not available, downloading {actualQuality.Label} instead");
            }
        }

        var fileName = outputPath ?? SanitizeFileName(video.Title);
        if (!Path.HasExtension(fileName))
        {
            fileName += audioOnly ? ".mp3" : ".mp4";
        }

        // If outputPath was provided and it's a directory, use video title as filename
        if (outputPath != null && Directory.Exists(outputPath))
        {
            fileName = Path.Combine(outputPath, SanitizeFileName(video.Title) + (audioOnly ? ".mp3" : ".mp4"));
        }

        var finalPath = Path.GetFullPath(fileName);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!_quiet)
        {
            Console.WriteLine($"Quality: {downloadOption.VideoQuality?.Label ?? "Audio Only"}");
            Console.WriteLine($"Output: {finalPath}");
            Console.WriteLine("Downloading...");
        }

        var progressBar = _quiet ? null : new ConsoleProgressBar();
        var progress = progressBar != null
            ? new Progress<Percentage>(p => progressBar.Update(p.Value))
            : null;

        await _downloader.DownloadVideoAsync(
            finalPath,
            video,
            downloadOption,
            includeSubtitles: false,
            progress,
            cancellationToken
        );

        progressBar?.Complete();

        if (!_quiet)
        {
            var fileInfo = new FileInfo(finalPath);
            Console.WriteLine($"\nDownloaded successfully: {FormatFileSize(fileInfo.Length)}");
        }

        return true;
    }

    private VideoQualityPreference GetQualityPreference(string quality)
    {
        return quality.ToLower() switch
        {
            "360p" => VideoQualityPreference.UpTo360p,
            "480p" => VideoQualityPreference.UpTo480p,
            "720p" => VideoQualityPreference.UpTo720p,
            "1080p" => VideoQualityPreference.UpTo1080p,
            "highest" => VideoQualityPreference.Highest,
            _ => VideoQualityPreference.UpTo1080p
        };
    }

    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName.Length > 200 ? fileName.Substring(0, 200) : fileName;
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class ConsoleProgressBar
{
    private readonly int _barWidth = 40;
    private int _lastPercentage = -1;
    private readonly object _lock = new object();
    private bool _completed = false;
    private readonly long _totalBytes;
    private readonly string _totalSizeStr;

    public ConsoleProgressBar(long totalBytes = 0)
    {
        _totalBytes = totalBytes;
        _totalSizeStr = totalBytes > 0 ? FormatFileSize(totalBytes) : "";
    }

    public void Update(double value)
    {
        lock (_lock)
        {
            if (_completed)
                return;

            // Clamp value between 0 and 1
            value = Math.Max(0, Math.Min(1, value));

            var percentage = (int)(value * 100);

            // Only update if percentage changed
            if (percentage == _lastPercentage)
                return;

            _lastPercentage = percentage;

            var completed = Math.Max(0, Math.Min(_barWidth, (int)(value * _barWidth)));
            var remaining = Math.Max(0, _barWidth - completed);

            try
            {
                // Clear the current line and redraw
                Console.Write("\r");
                Console.Write("[");
                Console.Write(new string('=', completed));
                Console.Write(new string(' ', remaining));
                Console.Write($"] {percentage,3}%");

                // Show actual bytes if available
                if (_totalBytes > 0)
                {
                    var downloadedBytes = (long)(_totalBytes * value);
                    var downloadedStr = FormatFileSize(downloadedBytes);
                    Console.Write($" ({downloadedStr}/{_totalSizeStr})");
                }

                if (percentage >= 100)
                {
                    Console.WriteLine();
                    _completed = true;
                }
            }
            catch
            {
                // Ignore console errors (e.g., when output is redirected)
            }
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    public void Complete()
    {
        lock (_lock)
        {
            if (!_completed)
            {
                Update(1.0);
                _completed = true;
            }
        }
    }
}