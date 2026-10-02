namespace PersonalWorkbench.Models;

public sealed record AreaCounty(string Code, string Name);

public sealed record AreaCity(string Code, string Name, IReadOnlyList<AreaCounty> Counties);

public sealed record AreaProvince(string Code, string Name, IReadOnlyList<AreaCity> Cities);

public sealed class IdentityGenerationRequest
{
    public string? ProvinceCode { get; set; }

    public string? CityCode { get; set; }

    public string? CountyCode { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string Gender { get; set; } = "random";

    public int Count { get; set; } = 5;
}

public sealed record GeneratedIdentity(
    string Name,
    string IdNumber,
    string Gender,
    DateOnly BirthDate,
    string AreaCode);

public sealed class SqlConversionRequest
{
    public string? InputSql { get; set; }
}

public sealed record SqlConversionResponse(string OutputSql);

public sealed class Base64ToImageRequest
{
    public string? Base64 { get; set; }

    public string? FileName { get; set; }
}

public sealed record ImageBase64Response(
    string FileName,
    string ContentType,
    long Size,
    string Base64,
    string DataUrl);

public sealed class CronGenerationRequest
{
    public string Frequency { get; set; } = "daily";

    public int Interval { get; set; } = 1;

    public int Minute { get; set; }

    public int Hour { get; set; }

    public int DayOfWeek { get; set; } = 1;

    public int DayOfMonth { get; set; } = 1;
}

public sealed record CronGenerationResponse(string Expression, string Description);

public sealed record PdfMergeInput(string FileName, Stream Content);

public sealed class ToolDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public int Order { get; set; }

    public string? Icon { get; set; }

    /// <summary>
    /// 返回配置图标；未配置时使用工具名称的第一个字符。
    /// </summary>
    public string DisplayIcon => string.IsNullOrWhiteSpace(Icon)
        ? Name.Trim().FirstOrDefault().ToString()
        : Icon.Trim();
}
