using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace YTDownloadCli;

/// <summary>
/// Manages multiple partial downloads in a directory
/// </summary>
public class PartialDownloadManager
{
    private readonly string _directory;

    public PartialDownloadManager(string directory)
    {
        _directory = directory;
    }

    /// <summary>
    /// Find all partial downloads in the directory
    /// </summary>
    public List<PartialDownloadInfo> GetPartialDownloads()
    {
        var partialDownloads = new List<PartialDownloadInfo>();

        // Find all .ytd_download metadata files
        var metadataFiles = Directory.GetFiles(_directory, "*.ytd_download", SearchOption.TopDirectoryOnly);

        foreach (var metadataFile in metadataFiles)
        {
            try
            {
                var json = File.ReadAllText(metadataFile);
                var metadata = JsonSerializer.Deserialize<DownloadMetadata>(json);

                if (metadata != null)
                {
                    var videoFile = metadataFile.Replace(".ytd_download", "");
                    var fileExists = File.Exists(videoFile);
                    var fileSize = fileExists ? new FileInfo(videoFile).Length : 0;

                    partialDownloads.Add(new PartialDownloadInfo
                    {
                        MetadataPath = metadataFile,
                        VideoPath = videoFile,
                        VideoId = metadata.VideoId,
                        VideoTitle = metadata.VideoTitle ?? metadata.VideoId,
                        TotalBytes = metadata.TotalBytes,
                        DownloadedBytes = fileExists ? fileSize : metadata.DownloadedBytes,
                        LastUpdate = metadata.LastUpdate,
                        RateLimitedAt = metadata.RateLimitedAt,
                        LastAttemptTime = metadata.LastAttemptTime,
                        StartTime = metadata.StartTime
                    });
                }
            }
            catch
            {
                // Ignore corrupt metadata files
            }
        }

        return partialDownloads.OrderBy(p => p.LastUpdate).ToList();
    }

    /// <summary>
    /// Find the best partial download to resume
    /// </summary>
    public PartialDownloadInfo? GetBestToResume()
    {
        var partials = GetPartialDownloads();

        // First, try to find a non-rate-limited partial download
        var nonRateLimited = partials
            .Where(p => !p.IsRateLimited())
            .OrderByDescending(p => p.GetCompletionPercentage())
            .FirstOrDefault();

        if (nonRateLimited != null)
            return nonRateLimited;

        // If all are rate-limited, find the one with the shortest wait time
        var shortestWait = partials
            .Where(p => p.IsRateLimited())
            .OrderBy(p => p.GetRateLimitRemaining() ?? TimeSpan.MaxValue)
            .FirstOrDefault();

        return shortestWait;
    }

    /// <summary>
    /// List all partial downloads with their status
    /// </summary>
    public void ListPartialDownloads(bool quiet = false)
    {
        var partials = GetPartialDownloads();

        if (partials.Count == 0)
        {
            if (!quiet)
                Console.WriteLine("No partial downloads found.");
            return;
        }

        Console.WriteLine("\n=== PARTIAL DOWNLOADS ===");
        Console.WriteLine($"Found {partials.Count} partial download(s) in {_directory}\n");

        int index = 1;
        foreach (var partial in partials)
        {
            Console.WriteLine($"[{index}] {partial.VideoTitle}");
            Console.WriteLine($"    Video ID: {partial.VideoId}");
            Console.WriteLine($"    File: {Path.GetFileName(partial.VideoPath)}");
            Console.WriteLine($"    Progress: {FormatFileSize(partial.DownloadedBytes)}/{FormatFileSize(partial.TotalBytes)} ({partial.GetCompletionPercentage():F1}%)");
            Console.WriteLine($"    Started: {partial.StartTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    Last update: {partial.LastUpdate:yyyy-MM-dd HH:mm:ss}");

            if (partial.IsRateLimited())
            {
                var remaining = partial.GetRateLimitRemaining();
                if (remaining != null)
                {
                    Console.WriteLine($"    ⚠ RATE LIMITED - Wait {FormatTimeSpan(remaining.Value)} before retry");
                }
            }
            else
            {
                Console.WriteLine($"    ✓ Ready to resume");
            }

            Console.WriteLine();
            index++;
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

    private string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
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

public class PartialDownloadInfo
{
    public string MetadataPath { get; set; } = "";
    public string VideoPath { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string VideoTitle { get; set; } = "";
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastUpdate { get; set; }
    public DateTime? RateLimitedAt { get; set; }
    public DateTime? LastAttemptTime { get; set; }

    public bool IsRateLimited()
    {
        if (RateLimitedAt == null)
            return false;

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

    public double GetCompletionPercentage()
    {
        if (TotalBytes == 0)
            return 0;
        return (double)DownloadedBytes / TotalBytes * 100;
    }
}