using PersonalWorkbench.Services;

var builder = WebApplication.CreateBuilder(args);

// 注册 Razor Pages、API Controller 和应用层服务到依赖注入容器。
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSingleton<IdentityGeneratorService>();
builder.Services.AddSingleton<SqlCaseConverterService>();
builder.Services.AddSingleton<ToolCatalogService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // 生产环境只返回统一错误页，避免向客户端暴露异常细节。
    app.UseExceptionHandler();
}

// 允许浏览器访问 wwwroot 中的 CSS、JavaScript 等静态资源。
app.UseStaticFiles();

// 启用路由并映射 API Controller 与 Razor Pages Endpoint。
app.UseRouting();

app.MapControllers();
app.MapRazorPages();

app.Run();
