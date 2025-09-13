using System.Collections.Concurrent;
using YoutubeExplode.Videos;
using Gress;

namespace YTDownloadServer.Services;

public class DownloadQueueService
{
    private readonly DownloadService _downloadService;
    private readonly ILogger<DownloadQueueService> _logger;
    private readonly ConcurrentQueue<DownloadTask> _queue = new();
    private readonly Dictionary<string, DownloadTask> _activeTasks = new();
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly object _statusLock = new();
    private int _completedCount = 0;
    private int _failedCount = 0;

    public int MaxConcurrentDownloads { get; }

    public DownloadQueueService(DownloadService downloadService, ILogger<DownloadQueueService> logger, int maxConcurrentDownloads = 2)
    {
        _downloadService = downloadService;
        _logger = logger;
        MaxConcurrentDownloads = maxConcurrentDownloads;
        _downloadSemaphore = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);

        _logger.LogInformation("=================================================");
        _logger.LogInformation("Download Queue Service Started");
        _logger.LogInformation($"Max concurrent downloads: {maxConcurrentDownloads}");
        _logger.LogInformation("=================================================");
    }

    public Task<string> EnqueueDownloadAsync(string videoId, string? videoTitle, string quality)
    {
        var taskId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var task = new DownloadTask
        {
            Id = taskId,
            VideoId = videoId,
            VideoTitle = videoTitle ?? $"video_{videoId}",
            Quality = quality,
            Status = DownloadStatus.Queued,
            QueuedAt = DateTime.UtcNow
        };

        _queue.Enqueue(task);

        LogStatus($"📥 NEW DOWNLOAD QUEUED", ConsoleColor.Cyan);
        _logger.LogInformation($"  ID: {taskId}");
        _logger.LogInformation($"  Video: {task.VideoTitle}");
        _logger.LogInformation($"  Quality: {quality}");
        PrintQueueStatus();

        _ = ProcessQueueAsync();

        return Task.FromResult(taskId);
    }

    private async Task ProcessQueueAsync()
    {
        while (_queue.TryDequeue(out var task))
        {
            await _downloadSemaphore.WaitAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessDownloadAsync(task);
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            });
        }
    }

    private async Task ProcessDownloadAsync(DownloadTask task)
    {
        lock (_statusLock)
        {
            task.Status = DownloadStatus.Downloading;
            task.StartedAt = DateTime.UtcNow;
            _activeTasks[task.Id] = task;
        }

        LogStatus($"⬇️  DOWNLOAD STARTED", ConsoleColor.Green);
        _logger.LogInformation($"  ID: {task.Id}");
        _logger.LogInformation($"  Video: {task.VideoTitle}");
        PrintActiveDownloads();

        try
        {
            var progress = new Progress<Percentage>(p =>
            {
                task.Progress = (int)(p.Value * 100);
                if (task.Progress % 20 == 0)
                {
                    _logger.LogInformation($"  [{task.Id}] Progress: {task.Progress}%");
                }
            });

            var result = await _downloadService.DownloadVideoAsync(
                task.VideoId,
                task.VideoTitle,
                task.Quality,
                progress
            );

            if (result.Success)
            {
                task.Status = DownloadStatus.Completed;
                task.CompletedAt = DateTime.UtcNow;
                task.FilePath = result.FilePath;

                lock (_statusLock)
                {
                    _completedCount++;
                    _activeTasks.Remove(task.Id);
                }

                LogStatus($"✅ DOWNLOAD COMPLETED", ConsoleColor.Green);
                _logger.LogInformation($"  ID: {task.Id}");
                _logger.LogInformation($"  Video: {task.VideoTitle}");
                _logger.LogInformation($"  File: {Path.GetFileName(result.FilePath)}");
                _logger.LogInformation($"  Duration: {(task.CompletedAt.Value - task.StartedAt.Value).TotalSeconds:F1}s");
            }
            else
            {
                throw new Exception(result.Message);
            }
        }
        catch (Exception ex)
        {
            task.Status = DownloadStatus.Failed;
            task.Error = ex.Message;
            task.CompletedAt = DateTime.UtcNow;

            lock (_statusLock)
            {
                _failedCount++;
                _activeTasks.Remove(task.Id);
            }

            LogStatus($"❌ DOWNLOAD FAILED", ConsoleColor.Red);
            _logger.LogError($"  ID: {task.Id}");
            _logger.LogError($"  Video: {task.VideoTitle}");
            _logger.LogError($"  Error: {ex.Message}");
        }

        PrintQueueStatus();
    }

    private void PrintQueueStatus()
    {
        _logger.LogInformation("-------------------------------------------------");
        _logger.LogInformation($"📊 Queue Status:");
        _logger.LogInformation($"  Active: {_activeTasks.Count}/{MaxConcurrentDownloads}");
        _logger.LogInformation($"  Queued: {_queue.Count}");
        _logger.LogInformation($"  Completed: {_completedCount}");
        _logger.LogInformation($"  Failed: {_failedCount}");
        _logger.LogInformation("-------------------------------------------------");
    }

    private void PrintActiveDownloads()
    {
        if (_activeTasks.Any())
        {
            _logger.LogInformation($"🔄 Active Downloads ({_activeTasks.Count}):");
            foreach (var task in _activeTasks.Values)
            {
                var duration = DateTime.UtcNow - task.StartedAt.GetValueOrDefault();
                _logger.LogInformation($"  [{task.Id}] {task.VideoTitle} - {task.Progress}% ({duration.TotalSeconds:F0}s)");
            }
        }
    }

    private void LogStatus(string message, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        _logger.LogInformation($"\n{message}");
        Console.ForegroundColor = originalColor;
    }

    public DownloadTask? GetTaskStatus(string taskId)
    {
        return _activeTasks.Values.FirstOrDefault(t => t.Id == taskId);
    }

    public IEnumerable<DownloadTask> GetAllTasks()
    {
        return _activeTasks.Values.Concat(_queue);
    }
}

public class DownloadTask
{
    public string Id { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string VideoTitle { get; set; } = "";
    public string Quality { get; set; } = "";
    public DownloadStatus Status { get; set; }
    public int Progress { get; set; }
    public string? Error { get; set; }
    public string? FilePath { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum DownloadStatus
{
    Queued,
    Downloading,
    Completed,
    Failed
}