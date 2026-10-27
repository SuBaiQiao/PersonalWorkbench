using System.Text.Json;
using PersonalWorkbench.Models;
using PersonalWorkbench.Services;

// 无第三方测试框架的控制台回归检查；任何断言失败都会抛出异常并使进程以失败状态退出。
// 执行方式：dotnet run --project tests/IdentityGeneratorChecks/IdentityGeneratorChecks.csproj

/// <summary>检查必要条件，并在失败时报告具体的区划或号码规则问题。</summary>
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

/// <summary>确认无效地区请求被业务异常拒绝；其他异常不吞掉，以便暴露真正的程序错误。</summary>
static void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException(message);
}

/// <summary>独立核对长度、地区前缀、出生日期、性别顺序码及末位校验码。</summary>
/// <param name="row">待验证的生成结果。</param>
/// <param name="gender">指定性别或 random；随机性别时不预设顺序码奇偶。</param>
/// <param name="countyCode">指定区县时要求结果严格使用该编码，为空时只检查内部一致性。</param>
static void CheckIdentity(GeneratedIdentity row, string gender, string? countyCode = null)
{
    Check(row.IdNumber.Length == 18, "身份证长度错误");
    Check(row.IdNumber[..6] == row.AreaCode, "区划与号码前缀不匹配");
    Check(countyCode is null || row.AreaCode == countyCode, "生成结果不属于指定区县");
    Check(row.IdNumber.Substring(6, 8) == row.BirthDate.ToString("yyyyMMdd"), "出生日期不匹配");
    if (gender != "random")
    {
        Check((row.IdNumber[16] - '0') % 2 == (gender == "male" ? 1 : 0), "性别顺序码错误");
    }
    int[] weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
    var sum = Enumerable.Range(0, 17).Sum(index => (row.IdNumber[index] - '0') * weights[index]);
    Check(row.IdNumber[17] == "10X98765432"[sum % 11], "身份证校验位错误");
}

var service = new IdentityGeneratorService();
var areas = service.GetAreas();
// 锁定当前快照的完整省级覆盖范围；83 是上游台湾省的自定义地址编码，不属于生成范围。
string[] provinceCodes = ["11", "12", "13", "14", "15", "21", "22", "23", "31", "32", "33", "34", "35", "36", "37", "41", "42", "43", "44", "45", "46", "50", "51", "52", "53", "54", "61", "62", "63", "64", "65", "81", "82", "83"];
Check(areas.Select(area => area.Code).SequenceEqual(provinceCodes.Select(code => code + "0000")), "全国省级地区覆盖不完整");
Check(areas.Count(area => area.SupportsIdentityGeneration) == 31, "身份证生成范围应覆盖大陆 31 个省级地区");

// 直接读取实际 API 程序集的嵌入资源，既验证发布配置，也逐项确认平铺表转换为树时没有丢数据。
using var sourceStream = typeof(IdentityGeneratorService).Assembly
    .GetManifestResourceStream("PersonalWorkbench.Data.china-areas.json")!;
var source = JsonSerializer.Deserialize<Dictionary<string, string>>(sourceStream)!;
var treeEntries = new Dictionary<string, string>();
var validCounties = new HashSet<string>();
var countyCount = 0;
// 遍历所有地区检查层级；对每个可生成叶节点分别验证男女号码，固定闰日检查日期原样保留。
foreach (var province in areas)
{
    treeEntries[province.Code] = province.Name;
    Check(province.Cities.Count > 0, $"省级地区无城市：{province.Name}");
    foreach (var city in province.Cities)
    {
        treeEntries[city.Code] = city.Name;
        Check(city.Counties.Count > 0, $"城市无区县：{city.Name}");
        foreach (var county in city.Counties)
        {
            treeEntries[county.Code] = county.Name;
            countyCount++;
            Check(county.Code.Length == 6 && county.Code.All(char.IsAsciiDigit), "区县必须使用六位编码");
            Check(county.Code.StartsWith(city.Code[..4]) && city.Code.StartsWith(province.Code[..2]), "地区层级不匹配");
            if (!province.SupportsIdentityGeneration) continue;
            Check(validCounties.Add(county.Code), $"区县重复：{county.Code}");
            foreach (var gender in new[] { "male", "female" })
            {
                var rows = service.Generate(new IdentityGenerationRequest
                {
                    ProvinceCode = province.Code, CityCode = city.Code, CountyCode = county.Code,
                    Gender = gender, Count = 1, BirthDate = new DateOnly(2000, 2, 29)
                });
                Check(rows.Count == 1, "生成数量错误");
                CheckIdentity(rows[0], gender, county.Code);
                Check(rows[0].BirthDate == new DateOnly(2000, 2, 29), "指定出生日期未生效");
            }
        }
    }
    if (province.SupportsIdentityGeneration)
    {
        // 仅指定省份时允许随机区县，但不得跨出该省的可选叶节点集合。
        var provinceCounties = province.Cities.SelectMany(city => city.Counties).Select(county => county.Code).ToHashSet();
        foreach (var row in service.Generate(new IdentityGenerationRequest { ProvinceCode = province.Code, Count = 100 }))
            Check(provinceCounties.Contains(row.AreaCode), "省级随机范围错误");
    }
    else
    {
        // 同时覆盖显式省份和只传区县两条路径，防止跳过省级参数后使用港澳台自定义编码。
        Reject(() => service.Generate(new IdentityGenerationRequest { ProvinceCode = province.Code }), "港澳台自定义编码被用于生成大陆身份证");
        Reject(() => service.Generate(new IdentityGenerationRequest { CountyCode = province.Cities[0].Counties[0].Code }), "仅指定港澳台区县时绕过了生成范围限制");
    }
}
foreach (var entry in source)
    Check(treeEntries.TryGetValue(entry.Key, out var name) && name == entry.Value, $"源数据条目遗漏：{entry.Key} {entry.Value}");

// 固定代表性地区的数量与特殊叶节点，防止数据更新时再次退化为少量示例数据。
Check(areas.Single(area => area.Code == "110000").Cities.Sum(city => city.Counties.Count) == 16, "北京区县不完整");
Check(areas.Single(area => area.Code == "310000").Cities.Sum(city => city.Counties.Count) == 16, "上海区县不完整");
Check(areas.Single(area => area.Code == "440000").Cities.Count == 21, "广东城市不完整");
foreach (var code in new[] { "441900", "442000", "460400", "620200", "419001", "429021", "469030", "659012" })
    Check(validCounties.Contains(code), $"特殊层级区划遗漏：{code}");

// 非法父子组合和未知编码必须被拒绝，不能静默改为全国随机生成。
Reject(() => service.Generate(new IdentityGenerationRequest { ProvinceCode = "110000", CityCode = "440100" }), "不匹配的省市组合未被拒绝");
Reject(() => service.Generate(new IdentityGenerationRequest { ProvinceCode = "440000", CityCode = "440100", CountyCode = "440305" }), "不匹配的市县组合未被拒绝");
Reject(() => service.Generate(new IdentityGenerationRequest { CountyCode = "999999" }), "无效区县未被拒绝");
// 全国随机与只指定市 / 县的调用路径也需保持相同编码范围和号码规则。
foreach (var row in service.Generate(new IdentityGenerationRequest { Count = 100 }))
{
    Check(validCounties.Contains(row.AreaCode), "全国随机生成使用了不可用编码");
    CheckIdentity(row, "random");
}
var cityOnly = service.Generate(new IdentityGenerationRequest { CityCode = "510100", Count = 100 });
Check(cityOnly.All(row => row.AreaCode.StartsWith("5101")), "仅按城市筛选失败");
Check(service.Generate(new IdentityGenerationRequest { CountyCode = "540102" }).All(row => row.AreaCode == "540102"), "仅按区县筛选失败");

Console.WriteLine($"PASS: {areas.Count} provinces, {areas.Sum(area => area.Cities.Count)} city groups, {countyCount} county leaves; {source.Count} source entries retained; {validCounties.Count} mainland leaves checked for both genders.");
