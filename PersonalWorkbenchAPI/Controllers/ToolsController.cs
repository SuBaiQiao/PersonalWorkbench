using Microsoft.AspNetCore.Mvc;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Controllers;

[ApiController]
[Route("api/tools")]
public sealed class ToolsController(ToolCatalogService toolCatalog) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<ToolDefinition>> GetTools() => Ok(toolCatalog.GetTools());
}
