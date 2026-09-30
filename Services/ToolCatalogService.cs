using System.Text.Json;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class ToolCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ToolCatalogService> _logger;

    /// <summary>
    /// 初始化工具目录服务。
    /// </summary>
    /// <param name="environment">用于定位项目根目录的宿主环境。</param>
    /// <param name="logger">记录配置文件读取异常的日志对象。</param>
    public ToolCatalogService(IWebHostEnvironment environment, ILogger<ToolCatalogService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// 读取、过滤并按配置顺序返回首页工具目录。
    /// </summary>
    /// <returns>可用的工具定义列表。</returns>
    public IReadOnlyList<ToolDefinition> GetTools()
    {
        var path = Path.Combine(_environment.ContentRootPath, "Data", "tools.json");

        if (!File.Exists(path))
        {
            _logger.LogWarning("Tool catalog was not found at {Path}.", path);
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            var tools = JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions) ?? [];

            // 过滤外部链接和无效配置，避免首页生成不受控的跳转地址。
            return tools
                .Where(IsUsableTool)
                .OrderBy(tool => tool.Order)
                .ThenBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Tool catalog at {Path} contains invalid JSON.", path);
            return [];
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Tool catalog at {Path} could not be read.", path);
            return [];
        }
    }

    /// <summary>
    /// 判断工具定义是否同时具备名称和本地路由。
    /// </summary>
    /// <param name="tool">待校验的工具定义。</param>
    /// <returns>配置可用于首页展示时返回 true。</returns>
    private static bool IsUsableTool(ToolDefinition tool)
    {
        return !string.IsNullOrWhiteSpace(tool.Name)
            && IsLocalRoute(tool.Route);
    }

    /// <summary>
    /// 校验路由是否为站内绝对路径，而不是外部链接或协议相对地址。
    /// </summary>
    /// <param name="route">待校验的路由。</param>
    /// <returns>路由安全且可用于站内跳转时返回 true。</returns>
    private static bool IsLocalRoute(string? route)
    {
        return !string.IsNullOrWhiteSpace(route)
            && route.StartsWith("/", StringComparison.Ordinal)
            && !route.StartsWith("//", StringComparison.Ordinal)
            && !route.Contains("://", StringComparison.Ordinal);
    }
}
