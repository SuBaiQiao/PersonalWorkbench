using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.Sqlite;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/database/manage")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2 * 1024 * 1024)]
public sealed class DatabaseManagementController(DatabaseManagementService manager, DatabaseAuditService audit) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (Request.Method is "POST" or "PUT" or "DELETE" && Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            context.Result = StatusCode(403, new { message = "数据库修改请求缺少必要的请求头。" });
        base.OnActionExecuting(context);
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is { } exception)
        {
            var status = exception switch
            {
                DatabaseRowConflictException => 409,
                SqliteException or ArgumentException or FormatException or OverflowException or System.Text.Json.JsonException => 400,
                IOException or UnauthorizedAccessException => 503,
                _ => 0
            };
            if (status != 0)
            {
                context.Result = StatusCode(status, new { message = status == 503 ? "无法持久化 SQL 日志，操作已停止。请检查日志目录的空间与权限；若执行期间发生故障，请刷新核对结果。" : exception.Message });
                context.ExceptionHandled = true;
            }
        }
        base.OnActionExecuted(context);
    }

    [HttpGet("tables")]
    public async Task<IActionResult> Tables(CancellationToken token) => Ok(await manager.TablesAsync(token));

    [HttpGet("structure")]
    public async Task<IActionResult> Structure(string table, CancellationToken token) => Ok(await manager.StructureAsync(table, token));

    [HttpGet("rows")]
    public async Task<IActionResult> Rows(string table, int page = 1, int pageSize = 20, string? search = null, string? sort = null, bool descending = false, CancellationToken token = default)
        => Ok(await manager.RowsAsync(table, page, pageSize, search, sort, descending, token));

    [HttpGet("logs")]
    public async Task<IActionResult> Logs(int page = 1, int pageSize = 20, string? search = null, string? status = null)
        => Ok(await audit.ListAsync(page, pageSize, search, status));

    [HttpPost("sql")]
    public async Task<IActionResult> Sql(DatabaseSqlRequest request, CancellationToken token) => Ok(await manager.ExecuteAsync(request.Sql, token));

    [HttpPost("indexes")]
    public async Task<IActionResult> CreateIndex(string table, DatabaseIndexRequest request, CancellationToken token) { await manager.CreateIndexAsync(table, request, token); return Ok(new { message = "索引已创建。" }); }

    [HttpDelete("index")]
    public async Task<IActionResult> DropIndex(string table, string name, CancellationToken token) { await manager.DropIndexAsync(table, name, token); return Ok(new { message = "索引已删除。" }); }

    [HttpPost("tables")]
    public async Task<IActionResult> Create(DatabaseTableRequest request, CancellationToken token) { await manager.CreateAsync(request, token); return Ok(new { message = "表已创建。" }); }

    [HttpPut("table")]
    public async Task<IActionResult> Rename(string table, DatabaseRenameRequest request, CancellationToken token) { await manager.RenameAsync(table, request.Name, token); return Ok(new { message = "表已重命名。" }); }

    [HttpDelete("table")]
    public async Task<IActionResult> Drop(string table, CancellationToken token) { await manager.TableActionAsync(table, true, token); return Ok(new { message = "表已删除。" }); }

    [HttpPost("clear")]
    public async Task<IActionResult> Clear(string table, CancellationToken token) { await manager.TableActionAsync(table, false, token); return Ok(new { message = "表已清空。" }); }

    [HttpPost("columns")]
    public async Task<IActionResult> AddColumn(string table, DatabaseColumnRequest request, CancellationToken token) { await manager.ColumnAsync(table, null, request, token); return Ok(new { message = "字段已添加。" }); }

    [HttpPut("column")]
    public async Task<IActionResult> ModifyColumn(string table, string column, DatabaseColumnRequest request, CancellationToken token) { await manager.ColumnAsync(table, column, request, token); return Ok(new { message = "字段已修改。" }); }

    [HttpDelete("column")]
    public async Task<IActionResult> DropColumn(string table, string column, CancellationToken token) { await manager.ColumnAsync(table, column, null, token); return Ok(new { message = "字段已删除。" }); }

    [HttpPost("rows")]
    public async Task<IActionResult> Insert(string table, DatabaseRowRequest request, CancellationToken token) { await manager.RowAsync(table, request, "insert", token); return Ok(new { message = "数据已新增。" }); }

    [HttpPut("row")]
    public async Task<IActionResult> Update(string table, DatabaseRowRequest request, CancellationToken token) { await manager.RowAsync(table, request, "update", token); return Ok(new { message = "数据已保存。" }); }

    [HttpPost("delete-row")]
    public async Task<IActionResult> DeleteRow(string table, DatabaseRowRequest request, CancellationToken token) { await manager.RowAsync(table, request, "delete", token); return Ok(new { message = "数据已删除。" }); }
}
