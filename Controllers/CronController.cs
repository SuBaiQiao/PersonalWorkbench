using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/cron")]
public sealed class CronController : ControllerBase
{
    private readonly CronGeneratorService _cronGenerator;

    /// <summary>
    /// 初始化 Cron 表达式接口控制器。
    /// </summary>
    /// <param name="cronGenerator">负责生成和校验 Cron 表达式的服务。</param>
    public CronController(CronGeneratorService cronGenerator)
    {
        _cronGenerator = cronGenerator;
    }

    /// <summary>
    /// 根据执行频率和时间参数生成标准五字段 Cron 表达式。
    /// </summary>
    /// <param name="request">频率、间隔、时间和日期参数。</param>
    /// <returns>生成结果，或包含参数错误信息的 400 响应。</returns>
    [HttpPost("generate")]
    public ActionResult<CronGenerationResponse> Generate([FromBody] CronGenerationRequest request)
    {
        try
        {
            return Ok(_cronGenerator.Generate(request));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
