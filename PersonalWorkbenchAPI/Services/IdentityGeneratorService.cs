using System.Text.Json;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>
/// 提供全国区划三级目录，并按地区、日期和性别生成开发测试用的虚构身份信息。
/// </summary>
public sealed class IdentityGeneratorService
{
    // 区划快照在类型首次初始化时加载一次，后续查询与生成复用同一棵只读目录树。
    private static readonly IReadOnlyList<AreaProvince> Areas = LoadAreas();

    /// <summary>
    /// 从程序集内的全国区划快照构建三级树，部署后无需网络或外部文件路径。
    /// </summary>
    /// <returns>按六位代码排序的省级目录，包含为三级联动补充的市级分组。</returns>
    /// <exception cref="InvalidOperationException">内置资源缺失或反序列化结果为空时抛出。</exception>
    private static IReadOnlyList<AreaProvince> LoadAreas()
    {
        // 资源逻辑名由项目文件固定，不能依赖进程工作目录，否则发布后的启动路径可能导致读取失败。
        using var stream = typeof(IdentityGeneratorService).Assembly
            .GetManifestResourceStream("PersonalWorkbench.Data.china-areas.json")
            ?? throw new InvalidOperationException("缺少全国行政区划数据。");
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("全国行政区划数据为空。");

        // 快照为“六位编码 -> 名称”的平铺表；末四位为 0 的条目作为省级入口。
        return entries.Where(entry => entry.Key.EndsWith("0000", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(province =>
            {
                // 同一省级地区共享前两位编码，排除省自身后再区分市级与县级条目。
                var children = entries.Where(entry => entry.Key.StartsWith(province.Key[..2], StringComparison.Ordinal)
                    && entry.Key != province.Key).ToArray();
                var cities = children.Where(entry => entry.Key.EndsWith("00", StringComparison.Ordinal))
                    .ToDictionary(entry => entry.Key, entry => entry.Value);
                // 用区县前四位补 00 得到所属市级分组键；没有实际父节点的情况在下方补齐。
                var counties = children.Where(entry => !entry.Key.EndsWith("00", StringComparison.Ordinal))
                    .GroupBy(entry => entry.Key[..4] + "00")
                    .ToDictionary(group => group.Key,
                        group => group.Select(entry => new AreaCounty(entry.Key, entry.Value)).ToArray());

                // 例如北京区县归入 110100，济源等省直辖县级单位归入 419000。
                // 这些分组用于联动显示，最终生成号码时始终读取真实叶节点的六位代码。
                foreach (var cityCode in counties.Keys.Where(code => !cities.ContainsKey(code)))
                {
                    cities[cityCode] = cityCode.Substring(2, 2) switch
                    {
                        "90" => province.Value.EndsWith("自治区", StringComparison.Ordinal)
                            ? "自治区直辖县级行政区划" : "省直辖县级行政区划",
                        "02" => "县",
                        _ => province.Key is "810000" or "820000" ? province.Value : "市辖区"
                    };
                }

                var cityTree = cities.OrderBy(city => city.Key, StringComparer.Ordinal)
                    .Select(city => new AreaCity(city.Key, city.Value,
                        counties.TryGetValue(city.Key, out var districts)
                            ? districts.OrderBy(county => county.Code, StringComparer.Ordinal).ToArray()
                            // 东莞、中山、儋州、嘉峪关等不设县级区划，以自身六位代码作为叶节点。
                            : [new AreaCounty(city.Key, city.Value)]))
                    .ToArray();

                // 上游港澳台编码为自定义地址编码，其中台湾省使用 830000。
                // 保留其原始层级供地区浏览，通过能力标记将它们排除在大陆身份证生成范围之外。
                return new AreaProvince(province.Key, province.Value, cityTree,
                    province.Key is not ("810000" or "820000" or "830000"));
            }).ToArray();
    }

    private static readonly char[] Surnames =
        "赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华金魏陶姜戚谢邹喻柏水窦章云苏潘葛奚范彭郎鲁韦昌马苗凤花方俞任袁柳酆鲍史唐费廉岑薛雷贺倪汤滕殷罗毕郝邬安常乐于时傅皮卞齐康伍余元卜顾孟平黄和穆萧尹姚邵湛汪祁毛禹狄米贝明臧计伏成戴谈宋茅庞熊纪舒屈项祝董梁杜阮蓝闵席季麻强贾路娄危江童颜郭梅盛林刁钟徐邱骆高夏蔡田樊胡凌霍虞万支柯昝管卢莫经房裘缪干解应宗丁宣贲邓郁单杭洪包诸左石崔吉钮龚程嵇邢滑裴陆荣翁".ToCharArray();

    private static readonly char[] MaleNames =
        "伟刚勇毅俊峰强军平保东文辉力明永健世广志义兴良海山仁波宁贵福生龙元全国胜学祥才发武新利清飞彬富顺信子杰涛昌成康星光天达安岩中茂进林有坚和彪博诚先敬震振壮会思群豪心邦承乐绍功松善厚庆磊民友裕河哲江超浩亮政谦亨奇固之轮翰朗伯宏言若鸣朋斌梁栋维启克伦翔旭鹏泽晨辰建家致树炎德行时泰盛雄琛钧冠策腾楠榕风航弘".ToCharArray();

    private static readonly char[] FemaleNames =
        "秀娟英华慧巧美娜静淑惠珠翠雅芝玉萍红娥玲芬芳燕彩春菊兰凤洁梅琳素云莲真环雪荣爱妹霞香月莺媛艳瑞凡佳嘉琼勤珍贞莉桂娣叶璧璐娅琦晶妍茜秋珊莎锦黛青倩婷姣婉娴瑾颖露瑶怡婵雁蓓纨仪荷丹蓉眉君琴蕊薇菁梦岚苑婕馨瑗琰韵融园艺咏卿聪澜纯毓悦昭冰爽琬茗羽希宁欣飘育滢馥筠柔竹霭凝晓欢霄枫芸菲寒伊亚宜可姬舒影荔枝思丽".ToCharArray();

    /// <summary>
    /// 返回可供前端级联选择的行政区划数据。
    /// </summary>
    /// <returns>省、市、区县三级行政区划树。</returns>
    public IReadOnlyList<AreaProvince> GetAreas() => Areas;

    /// <summary>
    /// 判断性别参数是否为支持的值。
    /// </summary>
    /// <param name="gender">random、male 或 female。</param>
    /// <returns>参数合法时返回 true。</returns>
    public bool IsSupportedGender(string? gender) => gender?.ToLowerInvariant() is "random" or "male" or "female";

    /// <summary>
    /// 按请求条件生成指定数量的虚构身份信息。
    /// </summary>
    /// <param name="request">地区、出生日期、性别和数量条件。</param>
    /// <returns>生成的身份信息列表。</returns>
    /// <exception cref="ArgumentException">指定地区不支持生成，或找不到匹配的行政区划时抛出。</exception>
    public IReadOnlyList<GeneratedIdentity> Generate(IdentityGenerationRequest request)
    {
        // 显式选中仅可浏览的地区时返回明确原因，不能将自定义地址编码拼入身份证号码。
        if (Areas.Any(area => area.Code == request.ProvinceCode && !area.SupportsIdentityGeneration))
        {
            throw new ArgumentException("港澳台地区支持区划浏览，不支持按当地自定义区划编码生成大陆居民身份证号码。");
        }

        var counties = ResolveCounties(request.ProvinceCode, request.CityCode, request.CountyCode);
        if (counties.Count == 0)
        {
            throw new ArgumentException("没有找到匹配的行政区划，请重新选择地区。");
        }

        var gender = request.Gender.ToLowerInvariant();
        var result = new List<GeneratedIdentity>(request.Count);

        for (var index = 0; index < request.Count; index++)
        {
            // 根据用户选择或随机决定性别，再生成满足奇男偶女规则的顺序码。
            var isMale = gender switch
            {
                "male" => true,
                "female" => false,
                _ => Random.Shared.Next(2) == 1
            };
            var birthDate = request.BirthDate ?? RandomBirthDate();
            var areaCode = counties[Random.Shared.Next(counties.Count)].Code;
            var sequence = Random.Shared.Next(0, 500) * 2 + (isMale ? 1 : 0);
            var idWithoutCheckDigit = $"{areaCode}{birthDate:yyyyMMdd}{sequence:000}";
            // 18 位身份证的最后一位由前 17 位按权重计算得到。
            var idNumber = idWithoutCheckDigit + CalculateCheckDigit(idWithoutCheckDigit);

            result.Add(new GeneratedIdentity(
                GenerateName(isMale),
                idNumber,
                isMale ? "男" : "女",
                birthDate,
                areaCode));
        }

        return result;
    }

    /// <summary>
    /// 根据省、市、区县筛选可用的区县代码；空条件表示随机范围。
    /// </summary>
    /// <param name="provinceCode">省级代码，可为空。</param>
    /// <param name="cityCode">城市代码，可为空。</param>
    /// <param name="countyCode">区县代码，可为空。</param>
    /// <returns>符合条件的区县列表。</returns>
    private static IReadOnlyList<AreaCounty> ResolveCounties(string? provinceCode, string? cityCode, string? countyCode)
    {
        var provinces = string.IsNullOrWhiteSpace(provinceCode)
            ? Areas
            : Areas.Where(area => area.Code == provinceCode).ToArray();

        // 即便只传入市或县代码，也先排除不支持生成的省级地区，避免绕过省份校验。
        var cities = provinces
            .Where(province => province.SupportsIdentityGeneration)
            .SelectMany(province => province.Cities)
            .Where(city => string.IsNullOrWhiteSpace(cityCode) || city.Code == cityCode);

        // 层层在父级结果中筛选，省、市、县组合不匹配时自然得到空结果，不会跨地区兜底。
        var counties = cities
            .SelectMany(city => city.Counties)
            .Where(county => string.IsNullOrWhiteSpace(countyCode) || county.Code == countyCode)
            .ToArray();

        return counties;
    }

    /// <summary>
    /// 在模板约定的出生日期范围内生成随机日期。
    /// </summary>
    /// <returns>1960-01-01 至 2005-12-31 之间的日期。</returns>
    private static DateOnly RandomBirthDate()
    {
        var start = new DateOnly(1960, 1, 1).DayNumber;
        var end = new DateOnly(2005, 12, 31).DayNumber;
        return DateOnly.FromDayNumber(Random.Shared.Next(start, end + 1));
    }

    /// <summary>
    /// 根据性别从姓名字库中生成一个单姓一名或单姓双名。
    /// </summary>
    /// <param name="isMale">是否使用男性名字字库。</param>
    /// <returns>随机生成的中文姓名。</returns>
    private static string GenerateName(bool isMale)
    {
        var pool = isMale ? MaleNames : FemaleNames;
        var nameLength = Random.Shared.NextDouble() > 0.3 ? 2 : 1;
        var name = new char[nameLength + 1];
        name[0] = Surnames[Random.Shared.Next(Surnames.Length)];

        for (var index = 1; index < name.Length; index++)
        {
            name[index] = pool[Random.Shared.Next(pool.Length)];
        }

        return new string(name);
    }

    /// <summary>
    /// 根据身份证前 17 位计算最后一位校验码。
    /// </summary>
    /// <param name="idWithoutCheckDigit">不包含校验码的 17 位字符串。</param>
    /// <returns>数字或 X 校验码。</returns>
    private static char CalculateCheckDigit(string idWithoutCheckDigit)
    {
        var weights = new[] { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
        const string checkDigits = "10X98765432";
        var sum = 0;

        for (var index = 0; index < weights.Length; index++)
        {
            sum += (idWithoutCheckDigit[index] - '0') * weights[index];
        }

        return checkDigits[sum % 11];
    }
}
