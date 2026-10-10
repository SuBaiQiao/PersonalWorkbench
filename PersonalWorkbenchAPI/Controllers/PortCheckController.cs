using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/port")]
public sealed class PortCheckController : ControllerBase
{
    private readonly PortCheckService _portCheck;

    public PortCheckController(PortCheckService portCheck)
    {
        _portCheck = portCheck;
    }

    [HttpPost("check")]
    public async Task<ActionResult<PortCheckResponse>> Check([FromBody] PortCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _portCheck.CheckAsync(request.Port, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("terminate")]
    public async Task<ActionResult<PortTerminateResponse>> Terminate([FromBody] PortTerminateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _portCheck.TerminateAsync(request.Port, request.ProcessId, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
