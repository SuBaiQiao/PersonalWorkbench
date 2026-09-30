using Microsoft.AspNetCore.Mvc.RazorPages;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

namespace PersonalWorkbench.Pages;

public sealed class IndexModel : PageModel
{
    private readonly ToolCatalogService _toolCatalog;

    /// <summary>
    /// 初始化首页页面模型。
    /// </summary>
    /// <param name="toolCatalog">读取首页工具目录的服务。</param>
    public IndexModel(ToolCatalogService toolCatalog)
    {
        _toolCatalog = toolCatalog;
    }

    public IReadOnlyList<ToolDefinition> Tools { get; private set; } = [];

    /// <summary>
    /// 处理首页 GET 请求并加载排序后的工具目录。
    /// </summary>
    public void OnGet()
    {
        Tools = _toolCatalog.GetTools();
    }
}
