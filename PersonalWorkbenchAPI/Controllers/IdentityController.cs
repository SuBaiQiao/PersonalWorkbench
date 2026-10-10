using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityController : ControllerBase
{
    private readonly IdentityGeneratorService _identityGenerator;

    /// <summary>
    /// 初始化身份信息接口控制器。
    /// </summary>
    /// <param name="identityGenerator">负责地区查询和测试身份信息生成的服务。</param>
    public IdentityController(IdentityGeneratorService identityGenerator)
    {
        _identityGenerator = identityGenerator;
    }

    /// <summary>
    /// 获取身份信息生成器使用的省、市、区县层级数据。
    /// </summary>
    /// <returns>行政区划树。</returns>
    [HttpGet("areas")]
    public ActionResult<IReadOnlyList<AreaProvince>> GetAreas()
    {
        return Ok(_identityGenerator.GetAreas());
    }

    /// <summary>
    /// 根据请求参数生成一批仅供测试使用的身份信息。
    /// </summary>
    /// <param name="request">地区、日期、性别和生成数量参数。</param>
    /// <returns>生成结果，或包含错误信息的 400 响应。</returns>
    [HttpPost("generate")]
    public IActionResult Generate([FromBody] IdentityGenerationRequest request)
    {
        // 在进入业务服务前拦截明显无效的请求，避免产生不必要的随机数据。
        if (request.Count is < 1 or > 100)
        {
            return BadRequest(new { message = "生成数量必须在 1 到 100 之间。" });
        }

        if (!_identityGenerator.IsSupportedGender(request.Gender))
        {
            return BadRequest(new { message = "性别参数只能是 random、male 或 female。" });
        }

        try
        {
            return Ok(_identityGenerator.Generate(request));
        }
        catch (ArgumentException exception)
        {
            // 将服务层的业务参数错误转换成前端可以直接展示的 400 响应。
            return BadRequest(new { message = exception.Message });
        }
    }
}
