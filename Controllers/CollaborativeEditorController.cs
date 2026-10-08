using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/collaborative")]
public sealed class CollaborativeEditorController : ControllerBase
{
    private readonly CollaborativeEditorService _editor;

    public CollaborativeEditorController(CollaborativeEditorService editor)
    {
        _editor = editor;
    }

    [HttpGet("state")]
    public ActionResult<CollaborativeEditorState> GetState() => Ok(_editor.GetState());

    [HttpPost("text")]
    public async Task<IActionResult> UpdateText([FromBody] CollaborativeTextUpdateRequest request, CancellationToken cancellationToken)
    {
        await _editor.UpdateTextAsync(request.Text, cancellationToken);
        return Ok(new { message = "文本已同步。" });
    }

    [HttpPost("files")]
    [RequestSizeLimit(CollaborativeEditorService.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CollaborativeEditorService.MaxUploadBytes)]
    public async Task<ActionResult<CollaborativeUploadResponse>> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择要上传的文件。" });
        }

        try
        {
            return Ok(await _editor.UploadAsync(file, cancellationToken));
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

    [HttpGet("files/{fileName}")]
    public IActionResult Download(string fileName)
    {
        var path = _editor.FindFile(fileName);
        return path is null ? NotFound(new { message = "文件不存在。" }) : PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }

    [HttpDelete("files/{fileName}")]
    public async Task<IActionResult> Delete(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _editor.DeleteAsync(fileName, cancellationToken);
            return deleted ? Ok(new { message = "文件删除成功。" }) : NotFound(new { message = "文件不存在。" });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
