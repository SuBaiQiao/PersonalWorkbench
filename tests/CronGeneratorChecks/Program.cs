using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

// dotnet run --project tests/CronGeneratorChecks/CronGeneratorChecks.csproj
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Reject(CronGeneratorService service, CronGenerationRequest request)
{
    try { service.Generate(request); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("无效 Cron 参数未被拒绝。");
}

var service = new CronGeneratorService();
Check(service.Generate(new() { Hour = 1, Minute = 9 }).Expression == "9 1 * * *", "旧版单小时请求不兼容。");
Check(service.Generate(new() { Format = "quartz", Hour = 1, Minute = 9 }).Expression == "0 9 1 * * ?", "单小时 Quartz 生成错误。");
var combined = service.Generate(new() { Format = "quartz", Hours = [12, 1, 12], Minute = 9 });
Check(combined.Expression == "0 9 1,12 * * ?", "多个小时未在一个 Quartz 表达式中去重、排序。");
Check(combined.Description == "每天 01:09:00、12:09:00 执行", "多时段中文说明错误。");
Check(service.Generate(new() { Hours = [1, 12], Minute = 9 }).Expression == "9 1,12 * * *", "五字段多小时生成错误。");

foreach (var format in new[] { "standard", "quartz" })
{
    var quartz = format == "quartz";
    foreach (var frequency in new[] { "minutely", "hourly", "daily", "weekly", "monthly" })
    {
        var result = service.Generate(new()
        {
            Format = format, Frequency = frequency, Interval = 5, Hour = 1,
            Hours = [1, 12], Minute = 9, Second = quartz ? 30 : 0, DayOfWeek = 1, DayOfMonth = 15
        });
        var standard = frequency switch
        {
            "minutely" => "*/5 * * * *",
            "hourly" => "9 1-23/5 * * *",
            "daily" => "9 1,12 * * *",
            "weekly" => "9 1,12 * * 1",
            _ => "9 1,12 15 * *"
        };
        var expected = !quartz ? standard : frequency == "weekly" ? "30 9 1,12 ? * 2" : "30 " + standard[..^1] + "?";
        Check(result.Expression == expected, $"{format}/{frequency} 格式错误：{result.Expression}");
        Check(!quartz || result.Description.Contains(frequency == "minutely" ? "第 30 秒" : "01:09:30"), "说明遗漏秒。");
    }
}
foreach (var day in Enumerable.Range(0, 7))
    Check(service.Generate(new() { Format = "quartz", Frequency = "weekly", DayOfWeek = day }).Expression == $"0 0 0 ? * {day + 1}", "Quartz 星期编号错误。");

foreach (var request in new CronGenerationRequest[]
{
    new() { Format = "bad" }, new() { Format = null! }, new() { Frequency = "bad" },
    new() { Hours = [] }, new() { Hours = [-1] }, new() { Hours = [24] }, new() { Hours = new int[25] },
    new() { Hour = 24 }, new() { Minute = 60 }, new() { Second = 1 },
    new() { Format = "quartz", Second = -1 }, new() { Format = "quartz", Second = 60 },
    new() { Frequency = "hourly", Hour = 24, Hours = [1] },
    new() { Frequency = "hourly", Interval = 24 }, new() { Frequency = "minutely", Interval = 0 },
    new() { Frequency = "weekly", DayOfWeek = 7 }, new() { Frequency = "monthly", DayOfMonth = 0 }
}) Reject(service, request);

Console.WriteLine("Cron generator checks passed: both formats, all frequencies, multiple hours, seconds, weekdays and invalid inputs.");
