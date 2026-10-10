using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/sql")]
public sealed class SqlController : ControllerBase
{
    private readonly SqlCaseConverterService _converter;

    /// <summary>
    /// 初始化 SQL 转换接口控制器。
    /// </summary>
    /// <param name="converter">负责驼峰命名转换的业务服务。</param>
    public SqlController(SqlCaseConverterService converter)
    {
        _converter = converter;
    }

    /// <summary>
    /// 将请求中的 SQL 驼峰命名转换为大写下划线命名。
    /// </summary>
    /// <param name="request">包含待转换文本的请求对象。</param>
    /// <returns>转换后的 SQL 结果。</returns>
    [HttpPost("convert")]
    public ActionResult<SqlConversionResponse> Convert([FromBody] SqlConversionRequest request)
    {
        return Ok(new SqlConversionResponse(_converter.Convert(request.InputSql)));
    }
}
