using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/amount")]
public sealed class AmountController : ControllerBase
{
    private readonly AmountUppercaseService _converter;

    public AmountController(AmountUppercaseService converter)
    {
        _converter = converter;
    }

    /// <summary>
    /// 将阿拉伯数字金额转换为人民币汉字大写。
    /// </summary>
    [HttpPost("convert")]
    public ActionResult<AmountConversionResponse> Convert([FromBody] AmountConversionRequest? request)
    {
        try
        {
            return Ok(new AmountConversionResponse(_converter.Convert(request?.InputAmount)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
