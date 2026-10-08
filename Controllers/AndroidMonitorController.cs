using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/android")]
public sealed class AndroidMonitorController : ControllerBase
{
    private readonly AndroidMonitorService _monitor;

    public AndroidMonitorController(AndroidMonitorService monitor)
    {
        _monitor = monitor;
    }

    [HttpGet("devices")]
    public ActionResult<AndroidDevicesResponse> ListDevices()
    {
        return Ok(new AndroidDevicesResponse(_monitor.GetDevices()));
    }

    [HttpPost("reconnect/{device}")]
    public async Task<ActionResult<AndroidReconnectResponse>> Reconnect(string device, CancellationToken cancellationToken)
    {
        try
        {
            var message = await _monitor.ReconnectAsync(device, cancellationToken);
            return Ok(new AndroidReconnectResponse("ok", message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
