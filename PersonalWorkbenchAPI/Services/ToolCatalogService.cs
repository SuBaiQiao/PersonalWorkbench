using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using PersonalWorkbench.Data;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class ToolCatalogService(WorkbenchDbContext db)
{
    public async Task<ToolCatalogResponse> GetCatalogAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        var tools = await db.ToolMenus.AsNoTracking().Where(tool => includeDisabled || tool.IsEnabled)
            .OrderBy(tool => tool.Order).ThenBy(tool => tool.Name).ToArrayAsync(cancellationToken);
        var categories = await db.MenuCategories.AsNoTracking().OrderBy(category => category.Order)
            .ThenBy(category => category.Id).ToArrayAsync(cancellationToken);
        // 数据库也可能由外部工具维护；公共目录继续过滤无效跳转，维护页保留这些记录供修正。
        var visibleTools = includeDisabled ? tools : tools.Where(tool => !string.IsNullOrWhiteSpace(tool.Name)
            && Regex.IsMatch(tool.Route, MenuRequest.RoutePattern)).ToArray();
        return new(visibleTools, categories);
    }

    public async Task<ToolDefinition?> SaveMenuAsync(int? id, MenuRequest request, CancellationToken cancellationToken)
    {
        var tool = id.HasValue ? await db.ToolMenus.FindAsync([id.Value], cancellationToken) : new ToolDefinition();
        if (tool is null) return null;
        if (!await db.MenuCategories.AnyAsync(category => category.Id == request.Category, cancellationToken))
            throw new ArgumentException("请选择已存在的菜单分类。");
        if (request.Features is null || request.Features.Any(feature => string.IsNullOrWhiteSpace(feature) || feature.Length > 100))
            throw new ArgumentException("功能标签不能为空，且每项不能超过 100 字符。");
        var route = request.Route.Trim();
        if (await db.ToolMenus.AnyAsync(menu => menu.Route == route && menu.Id != (id ?? 0), cancellationToken))
            throw new ArgumentException("菜单路由已存在，请使用不同的路径。");
        tool.Name = request.Name.Trim();
        tool.Route = route;
        tool.ComponentKey = request.ComponentKey;
        tool.Category = request.Category;
        tool.Order = request.Order;
        tool.Icon = request.Icon;
        tool.Tone = request.Tone;
        tool.Description = request.Description?.Trim() ?? string.Empty;
        tool.Keywords = request.Keywords?.Trim() ?? string.Empty;
        tool.Features = request.Features.Select(feature => feature.Trim()).Distinct().ToArray();
        tool.IsEnabled = request.IsEnabled;
        if (!id.HasValue) db.ToolMenus.Add(tool);
        await db.SaveChangesAsync(cancellationToken);
        return tool;
    }

    public async Task<bool> DeleteMenuAsync(int id, CancellationToken cancellationToken)
    {
        var tool = await db.ToolMenus.FindAsync([id], cancellationToken);
        if (tool is null) return false;
        db.ToolMenus.Remove(tool);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MenuCategory?> SaveCategoryAsync(string? id, MenuCategory request, CancellationToken cancellationToken)
    {
        if (request.Id == "all") throw new ArgumentException("all 是全部工具的保留标识。");
        if (id is not null && id != request.Id) throw new ArgumentException("分类标识不能修改，请新增分类后移动菜单。");
        var category = id is null ? new MenuCategory { Id = request.Id } : await db.MenuCategories.FindAsync([id], cancellationToken);
        if (category is null) return null;
        if (id is null && await db.MenuCategories.AnyAsync(item => item.Id == request.Id, cancellationToken))
            throw new ArgumentException("分类标识已存在。");
        category.Name = request.Name.Trim();
        category.Order = request.Order;
        if (id is null) db.MenuCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<bool> DeleteCategoryAsync(string id, CancellationToken cancellationToken)
    {
        var category = await db.MenuCategories.FindAsync([id], cancellationToken);
        if (category is null) return false;
        if (await db.ToolMenus.AnyAsync(tool => tool.Category == id, cancellationToken))
            throw new ArgumentException("该分类下仍有菜单，请先移动或删除这些菜单（包括禁用项）。");
        db.MenuCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
