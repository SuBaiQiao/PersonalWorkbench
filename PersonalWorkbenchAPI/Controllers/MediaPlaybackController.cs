using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

/// <summary>视频兼容播放接口。浏览器可直接支持的视频不必创建服务器会话。</summary>
[ApiController]
[Route("api/media")]
public sealed class MediaPlaybackController(MediaPlaybackService playback) : ControllerBase
{
    /// <summary>返回服务端兼容播放能力；不可用不妨碍前端直接播放。</summary>
    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities() => Ok(await playback.GetCapabilitiesAsync());

    /// <summary>根据网络地址启动独立转流会话，返回状态查询与播放列表入口。</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> Create([FromBody] MediaPlaybackRequest request)
    {
        try { return Ok(await playback.CreateAsync(request)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(503, new { message = exception.Message }); }
    }

    /// <summary>上传单个常见视频进行兼容播放；清单及分片集合由浏览器本地入口处理。</summary>
    [HttpPost("sessions/upload")]
    [RequestSizeLimit(MediaPlaybackService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaPlaybackService.MaxUploadBytes)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        try { return Ok(await playback.CreateUploadAsync(file, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(503, new { message = exception.Message }); }
    }

    /// <summary>查询转流状态并续期，暂停中的播放器也可通过心跳保持会话。</summary>
    [HttpGet("sessions/{id}")]
    public IActionResult Status(string id)
    {
        try { return Ok(playback.Get(id)); }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
    }

    /// <summary>按白名单读取清单或分片，提供 Range 响应以支持播放器分段读取。</summary>
    [HttpGet("sessions/{id}/{fileName}")]
    public IActionResult Media(string id, string fileName)
    {
        try
        {
            var stream = playback.OpenFile(id, fileName);
            Response.Headers.CacheControl = fileName.EndsWith(".m3u8") ? "no-store" : "private, max-age=60";
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(stream, fileName.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t", enableRangeProcessing: true);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FileNotFoundException or DirectoryNotFoundException)
        { return NotFound(new { message = "播放文件尚未就绪或会话已结束。" }); }
    }

    /// <summary>幂等释放会话，终止 FFmpeg 并清理专属临时目录。</summary>
    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> Stop(string id) { await playback.RemoveAsync(id); return NoContent(); }
}
