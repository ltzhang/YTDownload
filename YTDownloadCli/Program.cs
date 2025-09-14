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

var listPartialsOption = new Option<bool>(
    aliases: new[] { "--list-partials", "-l" },
    description: "List all partial downloads in the current directory",
    getDefaultValue: () => false
);

var retryRateLimitedOption = new Option<bool>(
    aliases: new[] { "--retry-limited" },
    description: "Retry all non-rate-limited partial downloads in the current directory",
    getDefaultValue: () => false
);

var userAgentOption = new Option<string?>(
    aliases: new[] { "--user-agent", "-u" },
    description: "User agent string (chrome, firefox, safari, edge, mobile, or custom string)",
    getDefaultValue: () => null
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
    audioOnlyOption,
    listPartialsOption,
    retryRateLimitedOption,
    userAgentOption
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
    var listPartials = context.ParseResult.GetValueForOption(listPartialsOption);
    var retryRateLimited = context.ParseResult.GetValueForOption(retryRateLimitedOption);
    var userAgent = context.ParseResult.GetValueForOption(userAgentOption);

    // Set custom user agent if specified
    if (!string.IsNullOrEmpty(userAgent))
    {
        var selectedAgent = userAgent.ToLower() switch
        {
            "chrome" => YoutubeDownloader.Core.Utils.Http.UserAgents.Chrome,
            "firefox" => YoutubeDownloader.Core.Utils.Http.UserAgents.Firefox,
            "safari" => YoutubeDownloader.Core.Utils.Http.UserAgents.Safari,
            "edge" => YoutubeDownloader.Core.Utils.Http.UserAgents.Edge,
            "mobile" => YoutubeDownloader.Core.Utils.Http.UserAgents.ChromeMobile,
            _ => userAgent // Use as custom string
        };

        YoutubeDownloader.Core.Utils.Http.SetUserAgent(selectedAgent);

        if (!quiet)
            Console.WriteLine($"Using user agent: {(userAgent.Length > 50 ? userAgent.Substring(0, 50) + "..." : userAgent)}");
    }

    // Handle list-partials command
    if (listPartials)
    {
        var manager = new PartialDownloadManager(outputDir);
        manager.ListPartialDownloads(quiet);
        Environment.Exit(0);
    }

    // Handle retry-limited command
    if (retryRateLimited)
    {
        var manager = new PartialDownloadManager(outputDir);
        var partials = manager.GetPartialDownloads()
            .Where(p => !p.IsRateLimited())
            .ToList();

        if (partials.Count == 0)
        {
            Console.WriteLine("No non-rate-limited partial downloads found to retry.");
            Environment.Exit(0);
        }

        Console.WriteLine($"Found {partials.Count} partial download(s) ready to resume:");
        var urls = new List<string>();
        foreach (var partial in partials)
        {
            Console.WriteLine($"  - {partial.VideoTitle} ({partial.GetCompletionPercentage():F1}% complete)");
            urls.Add($"https://www.youtube.com/watch?v={partial.VideoId}");
        }

        // Create a temporary file with URLs
        var tempFile = Path.GetTempFileName();
        await File.WriteAllLinesAsync(tempFile, urls);

        var retryDownloader = new YtdDownloader(quiet);
        var retryResult = await retryDownloader.DownloadFromFileAsync(
            tempFile,
            outputDir,
            quality,
            audioOnly,
            retries,
            context.GetCancellationToken()
        );

        File.Delete(tempFile);
        Environment.Exit(retryResult ? 0 : 1);
    }

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
        int rateLimited = 0;
        var rateLimitedUrls = new List<string>();

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

                // Check if this download is already complete or rate-limited
                var videoId = VideoId.TryParse(url);
                if (videoId != null)
                {
                    var tracker = new DownloadTracker(videoId.Value, outputPath);

                    // Check if file is already complete (file exists without metadata)
                    if (File.Exists(outputPath) && !File.Exists(tracker.MetadataPath))
                    {
                        var fileInfo = new FileInfo(outputPath);
                        if (fileInfo.Length > 0)
                        {
                            successful++;
                            if (!_quiet)
                                Console.WriteLine($"✓ Already downloaded (size: {FormatFileSize(fileInfo.Length)})");
                            continue; // Skip to next URL
                        }
                    }

                    // Check for partial download or rate limit
                    if (File.Exists(tracker.MetadataPath))
                    {
                        tracker.LoadMetadata();

                        if (tracker.IsRateLimited())
                        {
                            var remaining = tracker.GetRateLimitRemaining();
                            if (!_quiet)
                            {
                                Console.WriteLine($"⚠ Skipping (rate limited, wait {FormatTimeSpan(remaining ?? TimeSpan.Zero)})");
                            }
                            rateLimited++;
                            rateLimitedUrls.Add(url);
                            continue; // Skip to next URL
                        }
                        // Otherwise it's a resumable partial download - will be handled by DownloadWithRetryAsync
                    }
                }

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
                        Console.WriteLine($"✓ Successfully downloaded");
                }
                else
                {
                    // Check if it was rate limited
                    if (videoId != null)
                    {
                        var tracker = new DownloadTracker(videoId.Value, outputPath);
                        tracker.LoadMetadata();
                        if (tracker.IsRateLimited())
                        {
                            rateLimited++;
                            rateLimitedUrls.Add(url);
                            if (!_quiet)
                                Console.WriteLine($"⚠ Rate limited - will retry later");
                        }
                        else
                        {
                            failed++;
                            if (!_quiet)
                                Console.WriteLine($"✗ Failed to download");
                        }
                    }
                    else
                    {
                        failed++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Check for rate limit in exception
                if (ex.Message.Contains("Exceeded request rate limit") ||
                    ex.Message.Contains("too many requests") ||
                    ex.Message.Contains("429"))
                {
                    rateLimited++;
                    rateLimitedUrls.Add(url);
                    if (!_quiet)
                        Console.WriteLine($"⚠ Rate limited - continuing with next URL");
                }
                else
                {
                    failed++;
                    Console.Error.WriteLine($"Error processing URL: {ex.Message}");
                }
            }

            // Add a small delay between downloads to avoid rate limiting
            if (i < validUrls.Count - 1 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        if (!_quiet)
        {
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"Download Summary:");
            Console.WriteLine($"  ✓ Successful: {successful}/{validUrls.Count}");
            if (rateLimited > 0)
            {
                Console.WriteLine($"  ⚠ Rate limited: {rateLimited}/{validUrls.Count} (will retry later)");
            }
            if (failed > 0)
            {
                Console.WriteLine($"  ✗ Failed: {failed}/{validUrls.Count}");
            }
            Console.WriteLine(new string('=', 50));

            if (rateLimitedUrls.Count > 0)
            {
                Console.WriteLine($"\nRate-limited URLs saved for later retry:");
                var rateLimitedFile = Path.ChangeExtension(filePath, ".ratelimited.txt");
                await File.WriteAllLinesAsync(rateLimitedFile, rateLimitedUrls, cancellationToken);
                Console.WriteLine($"  {rateLimitedFile}");
                Console.WriteLine($"  Run again in 1-2 hours with: ytd -f \"{rateLimitedFile}\"");
            }
        }

        return failed == 0 && rateLimited == 0;
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
                // Check for rate limit error
                if (ex.Message.Contains("Exceeded request rate limit") ||
                    ex.Message.Contains("too many requests") ||
                    ex.Message.Contains("429"))
                {
                    if (!_quiet)
                        Console.Error.WriteLine($"⚠ Rate limited by YouTube. Please wait 1-2 hours before retrying.");

                    // Mark this download as rate limited
                    if (videoId != null)
                    {
                        var finalPath = outputPath ?? $"{videoId.Value.Value}.mp4";
                        var tempTracker = new DownloadTracker(videoId.Value.Value, finalPath);
                        tempTracker.LoadMetadata(); // Load existing metadata if any
                        tempTracker.RateLimitedAt = DateTime.UtcNow;
                        tempTracker.LastAttemptTime = DateTime.UtcNow;
                        tempTracker.SaveMetadata();
                    }

                    return false; // Don't retry on rate limit
                }

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

        // Check if file already exists
        if (File.Exists(finalPath))
        {
            // If metadata exists, it's an incomplete download
            if (File.Exists(tracker.MetadataPath))
            {
                // Will be handled by the resume logic below
            }
            else
            {
                // No metadata = download was completed successfully
                var fileInfo = new FileInfo(finalPath);
                if (fileInfo.Length > 0)
                {
                    if (!_quiet)
                    {
                        Console.WriteLine($"✓ File already downloaded: {finalPath}");
                        Console.WriteLine($"  Size: {FormatFileSize(fileInfo.Length)}");
                        Console.WriteLine("  Skipping download.");
                    }
                    return true;
                }
            }
        }

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
        tracker.VideoTitle = video.Title;
        tracker.DownloadUrl = video.Url;

        // Check if we can resume
        if (tracker.CanResume())
        {
            // Check if rate limited
            if (tracker.IsRateLimited())
            {
                var remaining = tracker.GetRateLimitRemaining();
                if (remaining != null)
                {
                    Console.Error.WriteLine($"⚠ This download was rate limited {remaining.Value.TotalMinutes:F0} minutes ago.");
                    Console.Error.WriteLine($"Please wait {FormatTimeSpan(remaining.Value)} before retrying.");

                    // Try to find another partial download to resume
                    var manager = new PartialDownloadManager(Path.GetDirectoryName(finalPath) ?? ".");
                    var alternative = manager.GetPartialDownloads()
                        .Where(p => p.VideoId != videoId.Value && !p.IsRateLimited())
                        .FirstOrDefault();

                    if (alternative != null)
                    {
                        Console.WriteLine($"\nAlternatively, you can resume: {alternative.VideoTitle}");
                        Console.WriteLine($"Run: ytd \"{alternative.VideoId}\" -o \"{alternative.VideoPath}\"");
                    }
                }
                return false;
            }

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
        else
        {
            // Fresh download - create metadata immediately
            tracker.SaveMetadata();
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

        // Update tracker with latest attempt time
        tracker.LastAttemptTime = DateTime.UtcNow;
        tracker.SaveMetadata();

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

    private string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
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