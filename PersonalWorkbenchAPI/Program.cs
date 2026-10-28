using Microsoft.EntityFrameworkCore;
using PersonalWorkbench.Data;
using PersonalWorkbench.Services;

var builder = WebApplication.CreateBuilder(args);

// API 与业务服务由后端提供，页面由 PersonalWorkbenchWeb 构建。
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=Data/personal-workbench.db";
builder.Services.AddDbContext<WorkbenchDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<IdentityGeneratorService>();
builder.Services.AddSingleton<FileManagerService>();
builder.Services.AddSingleton<TempCleanupService>();
builder.Services.AddSingleton<MediaPlaybackService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MediaPlaybackService>());
builder.Services.AddSingleton<SqlCaseConverterService>();
builder.Services.AddSingleton<AmountUppercaseService>();
builder.Services.AddScoped<ToolCatalogService>();
builder.Services.AddSingleton<PdfExpenseMergeService>();
builder.Services.AddSingleton<ImageBase64Service>();
builder.Services.AddSingleton<CronGeneratorService>();
builder.Services.AddSingleton<AndroidMonitorService>();
builder.Services.AddSingleton<PortCheckService>();
builder.Services.AddSingleton<ProcessNetworkMonitor>();
builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<CollaborativeEditorService>();
builder.Services.AddScoped<DeltaCardService>();
builder.Services.AddScoped<DatabaseTransferService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DatabaseAuditService>();
builder.Services.AddScoped<DatabaseManagementService>();
builder.Services.AddSingleton<CryptoService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AndroidMonitorService>());

// 开发环境可直接托管 npm run build 的结果；发布包统一使用 wwwroot。
var webBuildPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "PersonalWorkbenchWeb", "dist"));
if (builder.Environment.IsDevelopment() && Directory.Exists(webBuildPath))
{
    builder.Environment.WebRootPath = webBuildPath;
    builder.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webBuildPath);
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<WorkbenchDbContext>();
    await database.Database.EnsureCreatedAsync();
    await MenuDatabaseInitializer.InitializeAsync(database);
    await TempCleanupMenuInitializer.InitializeAsync(database);
}

if (!app.Environment.IsDevelopment())
{
    // 生产环境返回标准 ProblemDetails 错误响应，避免向客户端暴露异常细节。
    app.UseExceptionHandler();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/ws/collaborative", StringComparison.OrdinalIgnoreCase) && context.WebSockets.IsWebSocketRequest)
    {
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var editor = context.RequestServices.GetRequiredService<CollaborativeEditorService>();
        await editor.HandleWebSocketAsync(socket, context.RequestAborted);
        return;
    }

    const string webSocketPrefix = "/ws/android/";
    if (context.Request.Path.StartsWithSegments("/ws/android") && context.WebSockets.IsWebSocketRequest)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var device = path.StartsWith(webSocketPrefix, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(path[webSocketPrefix.Length..])
            : string.Empty;

        if (string.IsNullOrWhiteSpace(device))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var monitor = context.RequestServices.GetRequiredService<AndroidMonitorService>();
        await monitor.HandleWebSocketAsync(socket, device, context.RequestAborted);
        return;
    }

    await next();
});

// 未匹配的 API 与 WebSocket 请求保持 404，不返回 SPA 的 HTML。
app.UseRouting();

app.MapControllers();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/ws/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
