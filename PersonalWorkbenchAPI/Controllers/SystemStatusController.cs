using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemStatusController(SystemStatusService status) : ControllerBase
{
    [HttpGet("status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<SystemStatusResponse>> GetStatus(CancellationToken cancellationToken)
        => Ok(await status.GetStatusAsync(cancellationToken));

    [HttpGet("cpu")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<CpuDetailsResponse>> GetCpuDetails(CancellationToken cancellationToken)
        => Ok(await status.GetCpuDetailsAsync(cancellationToken));

    [HttpGet("gpu")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<GpuDetailsResponse>> GetGpuDetails(CancellationToken cancellationToken)
        => Ok(await status.GetGpuDetailsAsync(cancellationToken));

    [HttpGet("processes")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ProcessListResponse>> GetProcesses(CancellationToken cancellationToken)
        => Ok(await status.GetProcessesAsync(cancellationToken));
}
