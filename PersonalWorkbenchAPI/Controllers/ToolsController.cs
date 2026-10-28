using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/tools")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolsController(ToolCatalogService toolCatalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTools(CancellationToken cancellationToken)
        => Ok((await toolCatalog.GetCatalogAsync(false, cancellationToken)).Tools);

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
        => Ok(await toolCatalog.GetCatalogAsync(false, cancellationToken));

    [HttpGet("manage")]
    public async Task<IActionResult> GetManagementCatalog(CancellationToken cancellationToken)
        => Ok(await toolCatalog.GetCatalogAsync(true, cancellationToken));

    [HttpPost]
    public Task<IActionResult> CreateMenu(MenuRequest request, CancellationToken cancellationToken)
        => HandleAsync(async () => StatusCode(201, await toolCatalog.SaveMenuAsync(null, request, cancellationToken)));

    [HttpPut("{id:int}")]
    public Task<IActionResult> UpdateMenu(int id, MenuRequest request, CancellationToken cancellationToken)
        => HandleAsync(async () => await toolCatalog.SaveMenuAsync(id, request, cancellationToken) is { } menu ? Ok(menu) : NotFound());

    [HttpDelete("{id:int}")]
    public Task<IActionResult> DeleteMenu(int id, CancellationToken cancellationToken)
        => HandleAsync(async () => await toolCatalog.DeleteMenuAsync(id, cancellationToken) ? NoContent() : NotFound());

    [HttpPost("categories")]
    public Task<IActionResult> CreateCategory(MenuCategory request, CancellationToken cancellationToken)
        => HandleAsync(async () => StatusCode(201, await toolCatalog.SaveCategoryAsync(null, request, cancellationToken)));

    [HttpPut("categories/{id}")]
    public Task<IActionResult> UpdateCategory(string id, MenuCategory request, CancellationToken cancellationToken)
        => HandleAsync(async () => await toolCatalog.SaveCategoryAsync(id, request, cancellationToken) is { } category ? Ok(category) : NotFound());

    [HttpDelete("categories/{id}")]
    public Task<IActionResult> DeleteCategory(string id, CancellationToken cancellationToken)
        => HandleAsync(async () => await toolCatalog.DeleteCategoryAsync(id, cancellationToken) ? NoContent() : NotFound());

    private async Task<IActionResult> HandleAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        { return Conflict(new { message = "菜单或分类发生冲突，请刷新后重试；路由和分类标识不能重复，使用中的分类不能删除。" }); }
    }
}
