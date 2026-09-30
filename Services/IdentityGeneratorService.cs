using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class IdentityGeneratorService
{
    private static readonly IReadOnlyList<AreaProvince> Areas =
    [
        new AreaProvince("110000", "北京市",
        [
            new AreaCity("110100", "市辖区",
            [
                new AreaCounty("110101", "东城区"),
                new AreaCounty("110102", "西城区"),
                new AreaCounty("110105", "朝阳区"),
                new AreaCounty("110108", "海淀区")
            ])
        ]),
        new AreaProvince("310000", "上海市",
        [
            new AreaCity("310100", "市辖区",
            [
                new AreaCounty("310101", "黄浦区"),
                new AreaCounty("310104", "徐汇区"),
                new AreaCounty("310115", "浦东新区")
            ])
        ]),
        new AreaProvince("440000", "广东省",
        [
            new AreaCity("440100", "广州市",
            [
                new AreaCounty("440106", "天河区"),
                new AreaCounty("440111", "白云区"),
                new AreaCounty("440113", "番禺区")
            ]),
            new AreaCity("440300", "深圳市",
            [
                new AreaCounty("440303", "罗湖区"),
                new AreaCounty("440304", "福田区"),
                new AreaCounty("440305", "南山区")
            ])
        ])
    ];

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
    /// <exception cref="ArgumentException">找不到匹配的行政区划时抛出。</exception>
    public IReadOnlyList<GeneratedIdentity> Generate(IdentityGenerationRequest request)
    {
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

        var cities = provinces
            .SelectMany(province => province.Cities)
            .Where(city => string.IsNullOrWhiteSpace(cityCode) || city.Code == cityCode);

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
