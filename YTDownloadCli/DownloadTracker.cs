using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gress;

namespace YTDownloadCli;

/// <summary>
/// Tracks download progress and manages resumable downloads
/// </summary>
public class DownloadTracker
{
    private readonly string _metadataPath;
    private readonly object _lock = new object();
    private DateTime _lastProgressUpdate = DateTime.UtcNow;
    private double _lastProgress = 0;
    private bool _isStalled = false;
    private readonly TimeSpan _stallTimeout = TimeSpan.FromSeconds(10);

    public string VideoId { get; }
    public string OutputPath { get; }
    public string MetadataPath => _metadataPath;
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DateTime StartTime { get; }
    public string? DownloadUrl { get; set; }
    public DateTime? RateLimitedAt { get; set; }
    public DateTime? LastAttemptTime { get; set; }
    public string? VideoTitle { get; set; }

    public DownloadTracker(string videoId, string outputPath)
    {
        VideoId = videoId;
        OutputPath = outputPath;
        _metadataPath = $"{outputPath}.ytd_download";
        StartTime = DateTime.UtcNow;

        // Try to load existing metadata
        LoadMetadata();
    }

    public void UpdateProgress(double progress)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            // Check if progress has changed
            if (Math.Abs(progress - _lastProgress) > 0.001)
            {
                _lastProgress = progress;
                _lastProgressUpdate = now;
                _isStalled = false;

                // If we have a partial file, update TotalBytes estimate based on progress
                if (File.Exists(OutputPath) && progress > 0)
                {
                    var fileInfo = new FileInfo(OutputPath);
                    if (fileInfo.Length > 0 && TotalBytes == 0)
                    {
                        // Estimate total size based on current file size and progress
                        TotalBytes = (long)(fileInfo.Length / progress);
                    }
                    DownloadedBytes = fileInfo.Length;
                }
                else if (TotalBytes > 0)
                {
                    DownloadedBytes = (long)(TotalBytes * progress);
                }

                SaveMetadata();
            }
            else if (now - _lastProgressUpdate > _stallTimeout)
            {
                _isStalled = true;
            }
        }
    }

    public bool IsStalled()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastProgressUpdate > _stallTimeout)
            {
                _isStalled = true;
            }
            return _isStalled;
        }
    }

    public bool CanResume()
    {
        // Check if partial file exists
        if (!File.Exists(OutputPath))
            return false;

        // Check if metadata exists
        if (!File.Exists(_metadataPath))
            return false;

        // Check if partial file size matches metadata
        var fileInfo = new FileInfo(OutputPath);
        return fileInfo.Length > 0 && fileInfo.Length == DownloadedBytes;
    }

    public bool IsRateLimited()
    {
        if (RateLimitedAt == null)
            return false;

        // Consider rate limited for 2 hours
        var timeSinceLimit = DateTime.UtcNow - RateLimitedAt.Value;
        return timeSinceLimit.TotalHours < 2;
    }

    public TimeSpan? GetRateLimitRemaining()
    {
        if (RateLimitedAt == null)
            return null;

        var elapsed = DateTime.UtcNow - RateLimitedAt.Value;
        var twoHours = TimeSpan.FromHours(2);

        if (elapsed >= twoHours)
            return null;

        return twoHours - elapsed;
    }

    public void SaveMetadata()
    {
        try
        {
            var metadata = new DownloadMetadata
            {
                VideoId = VideoId,
                OutputPath = OutputPath,
                TotalBytes = TotalBytes,
                DownloadedBytes = DownloadedBytes,
                StartTime = StartTime,
                LastUpdate = DateTime.UtcNow,
                DownloadUrl = DownloadUrl,
                RateLimitedAt = RateLimitedAt,
                LastAttemptTime = LastAttemptTime,
                VideoTitle = VideoTitle
            };

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_metadataPath, json);
        }
        catch
        {
            // Ignore metadata save errors
        }
    }

    public void LoadMetadata()
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                var json = File.ReadAllText(_metadataPath);
                var metadata = JsonSerializer.Deserialize<DownloadMetadata>(json);

                if (metadata != null && metadata.VideoId == VideoId)
                {
                    TotalBytes = metadata.TotalBytes;
                    DownloadedBytes = metadata.DownloadedBytes;
                    DownloadUrl = metadata.DownloadUrl;
                    RateLimitedAt = metadata.RateLimitedAt;
                    LastAttemptTime = metadata.LastAttemptTime;
                    VideoTitle = metadata.VideoTitle;
                }
            }
        }
        catch
        {
            // Ignore metadata load errors
        }
    }

    public void Cleanup()
    {
        try
        {
            if (File.Exists(_metadataPath))
            {
                File.Delete(_metadataPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private class DownloadMetadata
    {
        public string VideoId { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime LastUpdate { get; set; }
        public string? DownloadUrl { get; set; }
        public DateTime? RateLimitedAt { get; set; }
        public DateTime? LastAttemptTime { get; set; }
        public string? VideoTitle { get; set; }
    }
}

/// <summary>
/// Progress handler that detects stalls and triggers retries
/// </summary>
public class StallDetectingProgress : IProgress<Percentage>, IDisposable
{
    private readonly DownloadTracker _tracker;
    private readonly IProgress<Percentage>? _innerProgress;
    private readonly CancellationTokenSource _stallCancellation;
    private readonly Timer _stallCheckTimer;
    private readonly Action<string> _onStallDetected;

    public StallDetectingProgress(
        DownloadTracker tracker,
        IProgress<Percentage>? innerProgress,
        Action<string> onStallDetected)
    {
        _tracker = tracker;
        _innerProgress = innerProgress;
        _onStallDetected = onStallDetected;
        _stallCancellation = new CancellationTokenSource();

        // Check for stalls every 5 seconds
        _stallCheckTimer = new Timer(
            CheckForStall,
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5)
        );
    }

    public void Report(Percentage value)
    {
        _tracker.UpdateProgress(value.Value);
        _innerProgress?.Report(value);
    }

    private void CheckForStall(object? state)
    {
        if (_tracker.IsStalled() && !_stallCancellation.Token.IsCancellationRequested)
        {
            _onStallDetected("Download stalled - no progress for 10 seconds");
            _stallCancellation.Cancel();
        }
    }

    public void Dispose()
    {
        _stallCheckTimer?.Dispose();
        _stallCancellation?.Dispose();
    }
}