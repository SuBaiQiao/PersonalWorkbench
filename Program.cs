using PersonalWorkbench.Services;

var builder = WebApplication.CreateBuilder(args);

// 注册 Razor Pages、API Controller 和应用层服务到依赖注入容器。
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSingleton<IdentityGeneratorService>();
builder.Services.AddSingleton<SqlCaseConverterService>();
builder.Services.AddSingleton<ToolCatalogService>();
builder.Services.AddSingleton<PdfExpenseMergeService>();
builder.Services.AddSingleton<ImageBase64Service>();
builder.Services.AddSingleton<CronGeneratorService>();
builder.Services.AddSingleton<AndroidMonitorService>();
builder.Services.AddSingleton<PortCheckService>();
builder.Services.AddSingleton<CollaborativeEditorService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AndroidMonitorService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // 生产环境只返回统一错误页，避免向客户端暴露异常细节。
    app.UseExceptionHandler();
}

// 允许浏览器访问 wwwroot 中的 CSS、JavaScript 等静态资源。
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

// 启用路由并映射 API Controller 与 Razor Pages Endpoint。
app.UseRouting();

app.MapControllers();
app.MapRazorPages();

app.Run();
