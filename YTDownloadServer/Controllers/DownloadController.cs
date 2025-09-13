using Microsoft.AspNetCore.Mvc;
using YTDownloadServer.Services;

namespace YTDownloadServer.Controllers;

[ApiController]
[Route("api")]
public class DownloadController : ControllerBase
{
    private readonly DownloadQueueService _queueService;
    private readonly ILogger<DownloadController> _logger;

    public DownloadController(DownloadQueueService queueService, ILogger<DownloadController> logger)
    {
        _queueService = queueService;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        _logger.LogInformation($"[{DateTime.UtcNow:HH:mm:ss}] Health check from {HttpContext.Connection.RemoteIpAddress}");
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download([FromBody] DownloadRequest request)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation("=================================================");
        _logger.LogInformation($"📨 NEW DOWNLOAD REQUEST");
        _logger.LogInformation($"  From: {clientIp}");
        _logger.LogInformation($"  Video ID: {request.VideoId}");
        _logger.LogInformation($"  Title: {request.VideoTitle ?? "Not provided"}");
        _logger.LogInformation($"  Quality: {request.Quality ?? "1080p"}");

        if (string.IsNullOrEmpty(request.VideoId))
        {
            _logger.LogWarning("  ⚠️ Request rejected: No video ID provided");
            return BadRequest(new { success = false, error = "Video ID is required" });
        }

        var taskId = await _queueService.EnqueueDownloadAsync(
            request.VideoId,
            request.VideoTitle,
            request.Quality ?? "1080p"
        );

        _logger.LogInformation($"  ✓ Queued with task ID: {taskId}");

        return Ok(new {
            success = true,
            taskId = taskId,
            message = "Download queued successfully"
        });
    }

    [HttpGet("status/{taskId}")]
    public IActionResult GetStatus(string taskId)
    {
        var task = _queueService.GetTaskStatus(taskId);
        if (task == null)
        {
            return NotFound(new { error = "Task not found" });
        }

        return Ok(new
        {
            taskId = task.Id,
            status = task.Status.ToString(),
            progress = task.Progress,
            videoTitle = task.VideoTitle,
            error = task.Error,
            filePath = task.FilePath
        });
    }

    [HttpGet("queue")]
    public IActionResult GetQueue()
    {
        var tasks = _queueService.GetAllTasks();
        return Ok(tasks.Select(t => new
        {
            taskId = t.Id,
            status = t.Status.ToString(),
            progress = t.Progress,
            videoTitle = t.VideoTitle,
            queuedAt = t.QueuedAt
        }));
    }
}

public class DownloadRequest
{
    public string VideoId { get; set; } = "";
    public string? VideoTitle { get; set; }
    public string? Quality { get; set; }
}