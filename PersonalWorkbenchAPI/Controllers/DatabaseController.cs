using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/database")]
public sealed class DatabaseController(DatabaseTransferService transfer) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var path = await transfer.CreateBackupAsync(cancellationToken);
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path, cancellationToken);
            return File(bytes, "application/vnd.sqlite3", $"personal-workbench-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }

    [HttpPost("import")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "请选择有效的 SQLite 数据库文件。" });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var count = await transfer.RestoreDatabaseAsync(stream, cancellationToken);
            return Ok(new { importedTables = count });
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (SqliteException)
        {
            return BadRequest(new { message = "文件不是有效或兼容的 SQLite 数据库备份。" });
        }
    }
}
