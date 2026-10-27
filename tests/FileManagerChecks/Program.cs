using System.Text;
using Microsoft.Extensions.Configuration;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

// 所有读写仅发生在本次测试创建的独立目录，不接触真实业务文件。
var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(workspace);
var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
var outside = Directory.CreateDirectory(Path.Combine(workspace, "root-other")).FullName;
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["FileManager:Roots:0"] = root, ["FileManager:MaxTextBytes"] = "4096"
}).Build();
var files = new FileManagerService(configuration);
var token = CancellationToken.None;
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("失败：" + name);
    checks++; Console.WriteLine("通过：" + name);
}
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, name); return; }
    throw new Exception("未拒绝：" + name);
}
try
{
    Check(files.GetCapabilities().Roots.Single().Path == root, "配置目录作为独立入口");
    var folder = await files.CreateAsync(new(root, "中文目录", true), token);
    var file = await files.CreateAsync(new(folder.Path, "说明.txt", false), token);
    var empty = await files.ReadTextAsync(file.Path, null, token);
    Check(empty.Content == "", "创建空文本文件");
    var first = await files.SaveTextAsync(new(file.Path, "中文内容\r\n第二行\r\n", empty.Version, "utf-8", true), token);
    Check((await File.ReadAllBytesAsync(file.Path)).AsSpan().StartsWith(new byte[] { 239, 187, 191 }), "UTF-8 BOM 被保存");
    Check((await files.ReadTextAsync(file.Path, null, token)).Content == first.Content, "中文与 CRLF 完整往返");
    var converted = await files.SaveTextAsync(new(file.Path, "编码转换🌱\n", first.Version, "utf-16", true), token);
    var readConverted = await files.ReadTextAsync(file.Path, null, token);
    Check(readConverted.Encoding == "utf-16" && readConverted.Content == converted.Content && readConverted.HasBom, "原字节校验不妨碍转换为 UTF-16");
    foreach (var targetEncoding in new[] { "utf-16be", "utf-32", "utf-32be", "utf-8" })
    {
        converted = await files.SaveTextAsync(new(file.Path, "大小端编码测试🌱\n", converted.Version, targetEncoding, true), token);
        var roundTrip = await files.ReadTextAsync(file.Path, null, token);
        Check(roundTrip.Encoding == targetEncoding && roundTrip.Content == converted.Content, targetEncoding + " 编码名称与内容完整往返");
    }
    await File.WriteAllTextAsync(file.Path, "外部修改", new UTF8Encoding(false));
    await Throws<FileManagerConflictException>(() => files.SaveTextAsync(new(file.Path, "不得覆盖", converted.Version, "utf-8", false), token), "外部修改触发保存冲突");
    Check(await File.ReadAllTextAsync(file.Path) == "外部修改", "冲突保留磁盘原文");
    var stale = await files.ReadTextAsync(file.Path, null, token);
    await files.SaveTextAsync(new(file.Path, "首次保存", stale.Version, "utf-8", false), token);
    await Throws<FileManagerConflictException>(() => files.SaveTextAsync(new(file.Path, "旧页面", stale.Version, "utf-8", false), token), "旧页面不能覆盖新版本");
    await Throws<FileManagerConflictException>(() => files.CreateAsync(new(folder.Path, "说明.txt", false), token), "同名创建不会覆盖");
    await Throws<ArgumentException>(() => files.CreateAsync(new(root, "../escape", false), token), "新建名称拒绝路径穿越");
    await Throws<UnauthorizedAccessException>(() => Task.FromResult(files.ValidatePath(Path.Combine(root, "..", "root-other", "a.txt"))), "规范化后拒绝越界路径");
    await Throws<UnauthorizedAccessException>(() => Task.FromResult(files.List(outside, null, false, "name", false, 0, 50, token)), "根目录边界不使用字符串前缀误判");
    if (OperatingSystem.IsWindows())
    {
        await Throws<ArgumentException>(() => files.CreateAsync(new(root, "CON.txt", false), token), "Windows 保留名称被拒绝");
        await Throws<ArgumentException>(() => Task.FromResult(files.ValidatePath(file.Path + ":hidden")), "拒绝 NTFS 数据流");
    }
    var binary = Path.Combine(root, "binary.bin");
    await File.WriteAllBytesAsync(binary, [0, 1, 2, 3]);
    await Throws<FileManagerFormatException>(() => files.ReadTextAsync(binary, null, token), "二进制文件不能误当文本编辑");
    var large = Path.Combine(root, "large.txt");
    await File.WriteAllTextAsync(large, new string('a', 4097));
    await Throws<FileManagerFormatException>(() => files.ReadTextAsync(large, null, token), "大文本读取有明确上限");
    var legacy = Path.Combine(root, "旧编码.txt");
    await File.WriteAllBytesAsync(legacy, Encoding.GetEncoding("gb18030").GetBytes("这是 GBK 兼容编码"));
    Check((await files.ReadTextAsync(legacy, "gb18030", token)).Content == "这是 GBK 兼容编码", "支持 GB18030 / GBK 文本");
    File.SetAttributes(file.Path, FileAttributes.ReadOnly);
    var readOnlyText = await files.ReadTextAsync(file.Path, null, token);
    await Throws<UnauthorizedAccessException>(() => files.SaveTextAsync(new(file.Path, "只读不写", readOnlyText.Version, "utf-8", false), token), "尊重文件只读属性");
    File.SetAttributes(file.Path, FileAttributes.Normal);
    var readOnlyConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FileManager:Roots:0"] = root, ["FileManager:ReadOnly"] = "true" }).Build();
    var readOnlyFiles = new FileManagerService(readOnlyConfig);
    await Throws<UnauthorizedAccessException>(() => readOnlyFiles.CreateAsync(new(root, "禁止创建", true), token), "配置只读模式拒绝新建");
    var listing = files.List(root, null, true, "name", false, 0, 2, token);
    Check(listing.ParentPath == null && listing.Total >= 4 && listing.Entries.Count == 2 && listing.Entries[0].IsDirectory, "目录分页、根边界、文件夹优先排序");
    var filtered = files.List(root, "旧编码", true, "size", true, 0, 50, token);
    Check(filtered.Total == 1 && filtered.Entries.Single().Name == "旧编码.txt", "按当前目录文件名搜索");
    var html = Path.Combine(root, "script.html");
    await File.WriteAllTextAsync(html, "<script>alert(1)</script>");
    var opened = files.OpenContent(html, false);
    using (opened.Stream) Check(!opened.Inline && opened.ContentType == "application/octet-stream", "HTML 下载为附件，不作为同源页面执行");
    Check(files.GetEntry(folder.Path).Size == null, "目录详情不递归遍历磁盘");
    Console.WriteLine($"全部 {checks} 项检查通过。");
}
finally
{
    // 测试清理前再次限定绝对路径，禁止递归删除其他位置。
    var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
    if (workspace.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(workspace).StartsWith("fixtures-"))
        Directory.Delete(workspace, true);
}
