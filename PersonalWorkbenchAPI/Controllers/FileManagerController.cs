using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net.Http.Headers;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

/// <summary>磁盘文件接口。JSON 写入需携带前端请求头，避免第三方网页通过表单触发修改。</summary>
[ApiController]
[Route("api/files")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FileManagerController(FileManagerService files) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (Request.Method is "POST" or "PUT" && Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            context.Result = StatusCode(403, new { message = "文件修改请求缺少必要的请求头。" });
        base.OnActionExecuting(context);
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is { } exception)
        {
            var (status, message) = exception switch
            {
                FileManagerConflictException => (409, exception.Message),
                FileManagerFormatException => (415, exception.Message),
                UnauthorizedAccessException => (403, "无权访问或修改该路径。" + exception.Message),
                FileNotFoundException or DirectoryNotFoundException => (404, "文件或文件夹已不存在，请刷新目录。"),
                ArgumentException or NotSupportedException or PathTooLongException => (400, exception.Message),
                IOException => (409, "文件正被占用、磁盘空间不足或磁盘暂不可用，请稍后重试。"),
                _ => (0, "")
            };
            if (status > 0)
            {
                context.Result = StatusCode(status, new { message });
                context.ExceptionHandled = true;
            }
        }
        base.OnActionExecuted(context);
    }

    [HttpGet("roots")]
    public IActionResult Roots() => Ok(files.GetCapabilities());

    [HttpGet("list")]
    public IActionResult List([FromQuery] string path, string? search, bool showHidden = false,
        string sort = "name", bool descending = false, int offset = 0, int limit = 100, CancellationToken cancellationToken = default)
        => Ok(files.List(path, search, showHidden, sort, descending, offset, limit, cancellationToken));

    [HttpGet("details")]
    public IActionResult Details([FromQuery] string path) => Ok(files.GetEntry(path));

    [HttpPost("entries")]
    public async Task<IActionResult> Create(CreateDiskEntryRequest request, CancellationToken cancellationToken)
        => Ok(await files.CreateAsync(request, cancellationToken));

    [HttpGet("text")]
    public async Task<IActionResult> Text([FromQuery] string path, string? encoding, CancellationToken cancellationToken)
        => Ok(await files.ReadTextAsync(path, encoding, cancellationToken));

    // JSON 对控制字符的转义会扩大请求体；限制最终文件字节数由服务层完成。
    [HttpPut("text")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Save(SaveFileTextRequest request, CancellationToken cancellationToken)
        => Ok(await files.SaveTextAsync(request, cancellationToken));

    [HttpGet("content")]
    public IActionResult ContentFile([FromQuery] string path, bool download = false)
    {
        var content = files.OpenContent(path, download);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        if (content.Inline)
            Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = content.Name }.ToString();
        // 支持 Range，视频拖动和 PDF 按需加载无需把整个文件读进内存。
        return content.Inline ? File(content.Stream, content.ContentType, enableRangeProcessing: true)
            : File(content.Stream, content.ContentType, content.Name, enableRangeProcessing: true);
    }
}
