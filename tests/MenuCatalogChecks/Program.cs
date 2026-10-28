using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using PersonalWorkbench.Controllers;
using PersonalWorkbench.Data;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

var root = Path.GetFullPath(Path.Combine("artifacts", "menu-db-verify", "run-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var dbPath = Path.Combine(root, "checks.db");
var options = Options(dbPath);
var token = CancellationToken.None;
int checks = 0;
using var seedStream = typeof(MenuDatabaseInitializer).Assembly.GetManifestResourceStream("PersonalWorkbench.Data.tools.json")!;
using var seedDocument = await JsonDocument.ParseAsync(seedStream);
var expectedMenus = seedDocument.RootElement.GetArrayLength();
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("失败：" + label);
    checks++; Console.WriteLine("通过：" + label);
}
async Task Reject<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); } catch (T) { Check(true, label); return; }
    throw new Exception("未拒绝：" + label);
}
static DbContextOptions<WorkbenchDbContext> Options(string path) => new DbContextOptionsBuilder<WorkbenchDbContext>()
    .UseSqlite($"Data Source={path};Pooling=False").Options;
MenuRequest Request(string name, string route, string category = "custom") => new()
{
    Name = name, Route = route, Category = category, ComponentKey = "SqlConverterView", Icon = "code", Tone = "blue",
    Features = ["自定义功能"], Keywords = "测试别名", Description = "测试说明", Order = -10
};

await using (var db = new WorkbenchDbContext(options))
{
    await db.Database.EnsureCreatedAsync();
    await MenuDatabaseInitializer.InitializeAsync(db);
    Check(await db.ToolMenus.CountAsync() == expectedMenus && await db.MenuCategories.CountAsync() == 4, "新库载入全部菜单和分类");
    var menu = await db.ToolMenus.FirstAsync();
    menu.Name = "自定义持久化名称";
    await db.SaveChangesAsync();
    await MenuDatabaseInitializer.InitializeAsync(db);
    Check(await db.ToolMenus.CountAsync() == expectedMenus && (await db.ToolMenus.AsNoTracking().FirstAsync()).Name == menu.Name, "重复初始化不覆盖维护数据");
    await db.ToolMenus.ExecuteUpdateAsync(set => set.SetProperty(menu => menu.IsEnabled, false));
    await MenuDatabaseInitializer.InitializeAsync(db);
    var catalog = new ToolCatalogService(db);
    Check((await catalog.GetCatalogAsync(false, token)).Tools.Count == 0 && (await catalog.GetCatalogAsync(true, token)).Tools.Count == expectedMenus, "全部禁用不触发重置，维护接口仍可读取");
    await db.ToolMenus.ExecuteUpdateAsync(set => set.SetProperty(menu => menu.IsEnabled, true));
    await catalog.SaveCategoryAsync(null, new MenuCategory { Id = "custom", Name = "自定义分类", Order = -1 }, token);
    var created = await catalog.SaveMenuAsync(null, Request("测试菜单", "/tools/custom-check"), token);
    Check(created is { Id: > 0 } && (await catalog.GetCatalogAsync(false, token)).Tools.First().Id == created.Id, "新增分类和菜单，排序从数据库生效");
    await Reject<ArgumentException>(() => catalog.SaveMenuAsync(null, Request("重复", "/tools/custom-check"), token), "重复路由被拒绝");
    await Reject<ArgumentException>(() => catalog.SaveMenuAsync(null, Request("错误分类", "/tools/missing", "missing"), token), "不存在的分类被拒绝");
    await Reject<ArgumentException>(() => catalog.DeleteCategoryAsync("custom", token), "使用中的分类不可删除");
    await Reject<ArgumentException>(() => catalog.SaveCategoryAsync(null, new MenuCategory { Id = "all", Name = "错误" }, token), "全部工具保留分类不可占用");
    var update = Request("更新菜单", "/tools/renamed-check");
    update.IsEnabled = false; update.Features = ["新功能"];
    await catalog.SaveMenuAsync(created!.Id, update, token);
    Check(!(await catalog.GetCatalogAsync(false, token)).Tools.Any(menu => menu.Id == created.Id), "禁用、修改路径和功能标签持久化");
    Check(await catalog.DeleteMenuAsync(created.Id, token) && await catalog.DeleteCategoryAsync("custom", token), "删除菜单后可删除分类");
    var invalidMenu = new ToolDefinition { Name = "外部维护的非法路径", Route = "//example.com", Category = "develop", ComponentKey = "SqlConverterView" };
    db.ToolMenus.Add(invalidMenu);
    await db.SaveChangesAsync();
    Check(!(await catalog.GetCatalogAsync(false, token)).Tools.Any(menu => menu.Id == invalidMenu.Id)
        && (await catalog.GetCatalogAsync(true, token)).Tools.Any(menu => menu.Id == invalidMenu.Id), "外部写入的非法菜单不进入公共导航，维护页可修复");
    await catalog.DeleteMenuAsync(invalidMenu.Id, token);
    db.ChangeTracker.Clear();
    var transfer = new DatabaseTransferService(db);
    var backup = await transfer.CreateBackupAsync(token);
    var savedName = (await db.ToolMenus.AsNoTracking().FirstAsync()).Name;
    await db.ToolMenus.ExecuteUpdateAsync(set => set.SetProperty(menu => menu.Name, "备份后修改"));
    await using (var stream = File.OpenRead(backup)) await transfer.RestoreDatabaseAsync(stream, token);
    Check((await db.ToolMenus.AsNoTracking().FirstAsync()).Name == savedName, "整库导出导入保留菜单配置");
}

var legacyPath = Path.Combine(root, "legacy.db");
await using (var legacy = new WorkbenchDbContext(Options(legacyPath)))
{
    await legacy.Database.EnsureCreatedAsync();
    await legacy.Database.ExecuteSqlRawAsync("DROP TABLE tool_menus; DROP TABLE menu_categories;");
    legacy.DeltaCards.Add(new DeltaCard { Name = "旧版卡牌", Category = "道具", Rarity = "普通", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
    await legacy.SaveChangesAsync();
}
await using (var db = new WorkbenchDbContext(options))
{
    await using (var stream = File.OpenRead(legacyPath)) await new DatabaseTransferService(db).RestoreDatabaseAsync(stream, token);
    Check(await db.ToolMenus.CountAsync() == expectedMenus && (await db.DeltaCards.SingleAsync()).Name == "旧版卡牌", "旧版备份先升级菜单，保留卡牌后导入");
}
await using (var legacy = new WorkbenchDbContext(Options(legacyPath)))
{
    await MenuDatabaseInitializer.InitializeAsync(legacy);
    Check(await legacy.ToolMenus.CountAsync() == expectedMenus && await legacy.DeltaCards.CountAsync() == 1, "直接启动旧库也能升级并保留卡牌");
    await legacy.ToolMenus.ExecuteDeleteAsync();
    await MenuDatabaseInitializer.InitializeAsync(legacy);
    Check(await legacy.ToolMenus.CountAsync() == expectedMenus, "真正空菜单再次初始化");
}

// 启动独立 HTTP 测试宿主，不启动 ADB、媒体等后台服务，不接触真实运行库。
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = Path.GetFullPath("PersonalWorkbenchWeb/dist") });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:5578");
builder.Services.AddDbContext<WorkbenchDbContext>(config => config.UseSqlite($"Data Source={dbPath};Pooling=False"));
builder.Services.AddScoped<ToolCatalogService>();
builder.Services.AddScoped<DatabaseTransferService>();
builder.Services.AddControllers().AddApplicationPart(typeof(ToolsController).Assembly);
await using var app = builder.Build();
app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5578") };
Check((await http.GetFromJsonAsync<ToolCatalogResponse>("/api/tools/catalog"))!.Tools.Count == expectedMenus, "HTTP 目录响应包含数据库配置");
foreach (var route in new[] { "//example.com", "/tools/bad?x=1", "/tools/a\\b", "/settings/menus" })
{
    var response = await http.PostAsJsonAsync("/api/tools", Request("非法路由", route, "develop"));
    Check(response.StatusCode == HttpStatusCode.BadRequest, "HTTP 拒绝非法或保留路由：" + route);
}
var valid = Request("HTTP 菜单", "/tools/http-check", "develop");
var post = await http.PostAsJsonAsync("/api/tools", valid);
var result = await post.Content.ReadFromJsonAsync<ToolDefinition>();
Check(post.StatusCode == HttpStatusCode.Created && result!.Features.Single() == "自定义功能", "HTTP 菜单新增与功能标签返回");
valid.IsEnabled = false;
Check((await http.PutAsJsonAsync("/api/tools/" + result!.Id, valid)).IsSuccessStatusCode, "HTTP 更新菜单");
Check(!(await http.GetFromJsonAsync<ToolCatalogResponse>("/api/tools/catalog"))!.Tools.Any(menu => menu.Id == result.Id), "HTTP 启用目录过滤禁用菜单");
Check((await http.DeleteAsync("/api/tools/" + result.Id)).StatusCode == HttpStatusCode.NoContent, "HTTP 删除菜单");
Check((await http.DeleteAsync("/api/tools/999999")).StatusCode == HttpStatusCode.NotFound, "HTTP 不存在菜单返回 404");
Console.WriteLine($"菜单验证通过：{checks} 项。独立数据目录：{root}");
if (args.Contains("--serve"))
{
    Console.WriteLine("UI 验证地址：http://127.0.0.1:5578/settings/menus");
    await app.WaitForShutdownAsync();
}
else await app.StopAsync();
