using System.Net;
using YoutubeDownloader.Core.Downloading;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Common;
using YoutubeExplode.Channels;
using Gress;

namespace YTDownloadServer.Services;

public class DownloadService
{
    private readonly VideoDownloader _downloader;
    private readonly string _downloadPath;

    public DownloadService()
    {
        _downloader = new VideoDownloader();
        _downloadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"
        );
    }

    public async Task<DownloadResult> DownloadVideoAsync(string videoId, string? videoTitle, string quality, IProgress<Percentage>? progress = null)
    {
        try
        {
            var vid = new VideoId(videoId);
            
            var preference = quality switch
            {
                "720p" => new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.UpTo720p
                ),
                _ => new VideoDownloadPreference(
                    Container.Mp4,
                    VideoQualityPreference.UpTo1080p
                )
            };

            var downloadOption = await _downloader.GetBestDownloadOptionAsync(
                vid,
                preference,
                includeLanguageSpecificAudioStreams: false
            );

            var safeTitle = SanitizeFileName(videoTitle ?? $"video_{videoId}");
            var fileName = $"{safeTitle}.mp4";
            var filePath = Path.Combine(_downloadPath, fileName);
            
            var uniqueFilePath = GetUniqueFilePath(filePath);

            await _downloader.DownloadVideoAsync(
                uniqueFilePath,
                new DummyVideo(vid, videoTitle ?? "YouTube Video"),
                downloadOption,
                includeSubtitles: false,
                progress: progress
            );

            return new DownloadResult
            {
                Success = true,
                FilePath = uniqueFilePath,
                Message = $"Downloaded to: {Path.GetFileName(uniqueFilePath)}"
            };
        }
        catch (Exception ex)
        {
            return new DownloadResult
            {
                Success = false,
                Message = $"Download failed: {ex.Message}"
            };
        }
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

    private string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        var directory = Path.GetDirectoryName(filePath)!;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        
        int counter = 1;
        string newFilePath;
        do
        {
            newFilePath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter}{extension}");
            counter++;
        } while (File.Exists(newFilePath));
        
        return newFilePath;
    }

    private class DummyVideo : IVideo
    {
        public VideoId Id { get; }
        public string Title { get; }
        public string Url => $"https://youtube.com/watch?v={Id}";
        public Author Author => new Author(ChannelId.Parse("UC0000000000000000000000"), "Unknown");
        public TimeSpan? Duration => null;
        public DateTimeOffset? UploadDate => null;
        public string Description => "";
        public IReadOnlyList<Thumbnail> Thumbnails => new List<Thumbnail>();

        public DummyVideo(VideoId id, string title)
        {
            Id = id;
            Title = title;
        }
    }
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string Message { get; set; } = "";
}