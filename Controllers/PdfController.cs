using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/pdf")]
public sealed class PdfController : ControllerBase
{
    private const long MaxRequestBytes = 100 * 1024 * 1024;
    private readonly PdfExpenseMergeService _pdfMergeService;

    /// <summary>
    /// 初始化报销发票 PDF 接口控制器。
    /// </summary>
    /// <param name="pdfMergeService">负责 PDF 分类和合并的服务。</param>
    public PdfController(PdfExpenseMergeService pdfMergeService)
    {
        _pdfMergeService = pdfMergeService;
    }

    /// <summary>
    /// 接收用户本次选择的 PDF 文件并返回合并后的 PDF 下载结果。
    /// </summary>
    /// <param name="files">浏览器通过 multipart/form-data 上传的 PDF 文件列表。</param>
    /// <returns>合并后的 PDF 文件，或包含校验错误的 400 响应。</returns>
    [HttpPost("expense-merge")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public IActionResult MergeExpensePdfs([FromForm] List<IFormFile> files)
    {
        var selectedFiles = files
            .Where(file => file.Length > 0)
            .ToList();

        if (selectedFiles.Count == 0)
        {
            return BadRequest(new { message = "请至少选择一个 PDF 文件。" });
        }

        var invalidFile = selectedFiles.FirstOrDefault(file =>
            !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase));

        if (invalidFile is not null)
        {
            return BadRequest(new { message = $"文件“{Path.GetFileName(invalidFile.FileName)}”不是 PDF 文件。" });
        }

        var streams = new List<Stream>(selectedFiles.Count);
        try
        {
            // 只为本次请求打开上传流；请求结束后在 finally 中统一释放。
            var inputs = new List<PdfMergeInput>(selectedFiles.Count);
            foreach (var file in selectedFiles)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                inputs.Add(new PdfMergeInput(Path.GetFileName(file.FileName), stream));
            }

            var mergedPdf = _pdfMergeService.Merge(inputs);
            return File(mergedPdf, "application/pdf", "合并后的PDF文件.pdf");
        }
        catch (InvalidDataException exception)
        {
            // 无效 PDF 属于用户输入错误，不应暴露成服务器异常。
            return BadRequest(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }
    }
}
