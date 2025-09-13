using Microsoft.AspNetCore.Mvc;
using YTDownloadServer.Services;

namespace YTDownloadServer.Controllers;

[ApiController]
[Route("api")]
public class DownloadController : ControllerBase
{
    private readonly DownloadService _downloadService;

    public DownloadController(DownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download([FromBody] DownloadRequest request)
    {
        if (string.IsNullOrEmpty(request.VideoId))
        {
            return BadRequest(new { success = false, error = "Video ID is required" });
        }

        var result = await _downloadService.DownloadVideoAsync(
            request.VideoId,
            request.VideoTitle,
            request.Quality ?? "1080p"
        );

        if (result.Success)
        {
            return Ok(new { success = true, message = result.Message, path = result.FilePath });
        }
        else
        {
            return StatusCode(500, new { success = false, error = result.Message });
        }
    }
}

public class DownloadRequest
{
    public string VideoId { get; set; } = "";
    public string? VideoTitle { get; set; }
    public string? Quality { get; set; }
}