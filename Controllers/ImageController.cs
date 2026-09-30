using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/image")]
public sealed class ImageController : ControllerBase
{
    private readonly ImageBase64Service _imageBase64Service;

    /// <summary>
    /// 初始化图片与 Base64 转换接口控制器。
    /// </summary>
    /// <param name="imageBase64Service">负责图片格式识别和 Base64 转换的服务。</param>
    public ImageController(ImageBase64Service imageBase64Service)
    {
        _imageBase64Service = imageBase64Service;
    }

    /// <summary>
    /// 将 Base64 或 Data URL 解码为图片文件并返回下载结果。
    /// </summary>
    /// <param name="request">包含 Base64 内容和可选文件名的请求。</param>
    /// <returns>图片文件，或包含输入错误信息的 400 响应。</returns>
    [HttpPost("base64-to-image")]
    [RequestSizeLimit(ImageBase64Service.MaxImageBytes * 2)]
    public IActionResult ConvertBase64ToImage([FromBody] Base64ToImageRequest request)
    {
        try
        {
            var result = _imageBase64Service.Decode(request);
            return File(result.Bytes, result.ContentType, result.FileName);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary>
    /// 将本次上传的图片转换为纯 Base64 和 Data URL。
    /// </summary>
    /// <param name="file">浏览器通过 multipart/form-data 上传的图片文件。</param>
    /// <returns>图片的 Base64 结果，或包含输入错误信息的 400 响应。</returns>
    [HttpPost("image-to-base64")]
    [RequestSizeLimit(ImageBase64Service.MaxImageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageBase64Service.MaxImageBytes)]
    public IActionResult ConvertImageToBase64([FromForm] IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择一张图片。" });
        }

        try
        {
            using var stream = file.OpenReadStream();
            var result = _imageBase64Service.Encode(stream, Path.GetFileName(file.FileName), file.ContentType, file.Length);
            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
