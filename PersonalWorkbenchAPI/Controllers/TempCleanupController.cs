using System.Net;
using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

/// <summary>操作当前后端账户的 TEMP；删除和自动任务配置仅接受来自本机的明确请求。</summary>
[ApiController]
[Route("api/temp-cleanup")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TempCleanupController(TempCleanupService cleanup) : ControllerBase
{
    private bool IsLocalRequest()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address == null || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)) return false;
        // 同时检查浏览器来源与代理转发地址，避免局域网客户端借开发代理获得本机操作资格。
        if (Request.Headers.Origin.Count > 0 && (!Uri.TryCreate(Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin) || !origin.IsLoopback)) return false;
        foreach (var forwarded in Request.Headers["X-Forwarded-For"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!IPAddress.TryParse(forwarded, out var remote) || !IPAddress.IsLoopback(remote)) return false;
        return true;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        try { return Ok((await cleanup.GetStatusAsync(cancellationToken)) with { CanManage = IsLocalRequest() }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(CancellationToken cancellationToken)
    {
        try { return Ok(await cleanup.PreviewAsync(cancellationToken)); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPost("clean")]
    public async Task<IActionResult> Clean(TempCleanupCleanRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest() || Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            return StatusCode(403, new { message = "请通过本机 localhost 地址打开工具台后执行清理。" });
        try { return Ok(await cleanup.CleanAsync(request.ExpectedTempPath, cancellationToken)); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpPut("automation")]
    public async Task<IActionResult> Automation(TempCleanupAutomationRequest request, CancellationToken cancellationToken)
    {
        if (!IsLocalRequest() || Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            return StatusCode(403, new { message = "请通过本机 localhost 地址打开工具台后配置自动清理。" });
        try { return Ok((await cleanup.SetAutomationAsync(request.Enabled!.Value, request.ExpectedTempPath, cancellationToken)) with { CanManage = true }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }
}
