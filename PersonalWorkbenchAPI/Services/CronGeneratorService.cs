using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class CronGeneratorService
{
    private static readonly IReadOnlyDictionary<int, string> DayNames =
        new Dictionary<int, string>
        {
            [0] = "星期日",
            [1] = "星期一",
            [2] = "星期二",
            [3] = "星期三",
            [4] = "星期四",
            [5] = "星期五",
            [6] = "星期六"
        };

    /// <summary>
    /// 根据执行频率和时间参数生成五字段 Cron 或六字段 Quartz 表达式。
    /// </summary>
    /// <param name="request">频率、时间、间隔和日期参数。</param>
    /// <returns>标准 Cron 表达式及对应的中文说明。</returns>
    /// <exception cref="ArgumentException">请求参数不在支持范围内时抛出。</exception>
    public CronGenerationResponse Generate(CronGenerationRequest request)
    {
        var format = request.Format?.Trim().ToLowerInvariant();
        if (format is not ("standard" or "quartz"))
            throw new ArgumentException("表达式格式只能是 standard 或 quartz。");
        ValidateRange(request.Second, 0, 59, "秒");
        if (format == "standard" && request.Second != 0)
            throw new ArgumentException("五字段 Cron 仅支持第 0 秒执行；指定秒请使用六字段 Quartz 格式。");
        var frequency = request.Frequency?.Trim().ToLowerInvariant();
        var result = frequency switch
        {
            "minutely" => GenerateMinutely(request),
            "hourly" => GenerateHourly(request),
            "daily" => GenerateDaily(request),
            "weekly" => GenerateWeekly(request),
            "monthly" => GenerateMonthly(request),
            _ => throw new ArgumentException("执行频率只能是 minutely、hourly、daily、weekly 或 monthly。", nameof(request))
        };
        if (format == "standard") return result;

        var fields = result.Expression.Split(' ');
        if (frequency == "weekly")
        {
            fields[2] = "?";
            fields[4] = (request.DayOfWeek + 1).ToString();
        }
        else fields[4] = "?";
        return result with
        {
            Expression = $"{request.Second} {string.Join(" ", fields)}",
            Description = frequency == "minutely" ? $"{result.Description}，在第 {request.Second} 秒执行" : result.Description
        };
    }

    /// <summary>
    /// 生成按固定分钟间隔执行的表达式。
    /// </summary>
    /// <param name="request">包含分钟间隔的请求。</param>
    /// <returns>每 N 分钟执行一次的 Cron 结果。</returns>
    private static CronGenerationResponse GenerateMinutely(CronGenerationRequest request)
    {
        ValidateRange(request.Interval, 1, 59, "分钟间隔");
        var expression = $"*/{request.Interval} * * * *";
        return new CronGenerationResponse(expression, $"每 {request.Interval} 分钟执行一次");
    }

    /// <summary>
    /// 生成按固定小时间隔执行的表达式。
    /// </summary>
    /// <param name="request">包含小时、分钟和小时间隔的请求。</param>
    /// <returns>每 N 小时执行一次的 Cron 结果。</returns>
    private static CronGenerationResponse GenerateHourly(CronGenerationRequest request)
    {
        ValidateTime(request);
        ValidateRange(request.Interval, 1, 23, "小时间隔");
        var expression = $"{request.Minute} {request.Hour}-23/{request.Interval} * * *";
        return new CronGenerationResponse(expression, $"从每天 {FormatTimes(request, [request.Hour])} 的时间位置开始，每 {request.Interval} 小时执行一次");
    }

    /// <summary>
    /// 生成每天固定时间执行的表达式。
    /// </summary>
    /// <param name="request">包含执行时间的请求。</param>
    /// <returns>每天在选定小时执行的 Cron 结果。</returns>
    private static CronGenerationResponse GenerateDaily(CronGenerationRequest request)
    {
        var hours = GetHours(request);
        var expression = $"{request.Minute} {string.Join(",", hours)} * * *";
        return new CronGenerationResponse(expression, $"每天 {FormatTimes(request, hours)} 执行");
    }

    /// <summary>
    /// 生成每周固定星期和时间执行的表达式。
    /// </summary>
    /// <param name="request">包含星期和执行时间的请求。</param>
    /// <returns>每周执行一次的 Cron 结果。</returns>
    private static CronGenerationResponse GenerateWeekly(CronGenerationRequest request)
    {
        var hours = GetHours(request);
        ValidateRange(request.DayOfWeek, 0, 6, "星期");
        var expression = $"{request.Minute} {string.Join(",", hours)} * * {request.DayOfWeek}";
        return new CronGenerationResponse(expression, $"每周{DayNames[request.DayOfWeek]} {FormatTimes(request, hours)} 执行");
    }

    /// <summary>
    /// 生成每月固定日期和时间执行的表达式。
    /// </summary>
    /// <param name="request">包含月份日期和执行时间的请求。</param>
    /// <returns>每月执行一次的 Cron 结果。</returns>
    private static CronGenerationResponse GenerateMonthly(CronGenerationRequest request)
    {
        var hours = GetHours(request);
        ValidateRange(request.DayOfMonth, 1, 31, "月份日期");
        var expression = $"{request.Minute} {string.Join(",", hours)} {request.DayOfMonth} * *";
        return new CronGenerationResponse(expression, $"每月 {request.DayOfMonth} 日 {FormatTimes(request, hours)} 执行");
    }

    private static int[] GetHours(CronGenerationRequest request)
    {
        ValidateRange(request.Minute, 0, 59, "分钟");
        var hours = request.Hours ?? [request.Hour];
        if (hours.Length is < 1 or > 24) throw new ArgumentException("请选择 1 到 24 个执行小时。");
        foreach (var hour in hours) ValidateRange(hour, 0, 23, "小时");
        return hours.Distinct().Order().ToArray();
    }

    private static string FormatTimes(CronGenerationRequest request, int[] hours)
    {
        var withSeconds = string.Equals(request.Format?.Trim(), "quartz", StringComparison.OrdinalIgnoreCase);
        return string.Join("、", hours.Select(hour => withSeconds
            ? $"{hour:D2}:{request.Minute:D2}:{request.Second:D2}"
            : $"{hour:D2}:{request.Minute:D2}"));
    }

    /// <summary>
    /// 校验小时和分钟是否为标准 Cron 时间字段允许的值。
    /// </summary>
    /// <param name="request">待校验的 Cron 请求。</param>
    private static void ValidateTime(CronGenerationRequest request)
    {
        ValidateRange(request.Minute, 0, 59, "分钟");
        ValidateRange(request.Hour, 0, 23, "小时");
    }

    /// <summary>
    /// 校验整数是否位于指定的闭区间内。
    /// </summary>
    /// <param name="value">待校验的整数。</param>
    /// <param name="minimum">允许的最小值。</param>
    /// <param name="maximum">允许的最大值。</param>
    /// <param name="fieldName">字段的中文名称。</param>
    private static void ValidateRange(int value, int minimum, int maximum, string fieldName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentException($"{fieldName}必须在 {minimum} 到 {maximum} 之间。");
        }
    }
}
