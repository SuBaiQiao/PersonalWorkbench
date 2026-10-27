namespace PersonalWorkbench.Models;

/// <summary>三级联动中的可选叶节点；不设区县的城市使用城市自身的六位代码。</summary>
/// <param name="Code">六位地区代码，生成能力由所属省级节点决定。</param>
/// <param name="Name">地区显示名称。</param>
public sealed record AreaCounty(string Code, string Name);

/// <summary>市级行政区或为保持三级联动而补充的分组，例如“省直辖县级行政区划”。</summary>
/// <param name="Code">市级行政区或联动分组的六位代码。</param>
/// <param name="Name">城市或分组名称。</param>
/// <param name="Counties">所属区县与特殊城市叶节点。</param>
public sealed record AreaCity(string Code, string Name, IReadOnlyList<AreaCounty> Counties);

/// <summary>省级区划节点，将地区浏览与身份证生成能力分开表达。</summary>
/// <param name="Code">省级地区代码；港澳台保留数据源自定义编码，详见快照说明。</param>
/// <param name="Name">省、自治区、直辖市或特别行政区的名称。</param>
/// <param name="Cities">所属城市与联动分组。</param>
/// <param name="SupportsIdentityGeneration">是否允许使用该节点下的代码生成大陆居民身份证测试号码。</param>
public sealed record AreaProvince(
    string Code,
    string Name,
    IReadOnlyList<AreaCity> Cities,
    bool SupportsIdentityGeneration = true);

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

public sealed class AmountConversionRequest
{
    public string? InputAmount { get; set; }
}

public sealed record AmountConversionResponse(string OutputAmount);

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
