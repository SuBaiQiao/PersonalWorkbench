using System.Text;
using System.Text.RegularExpressions;

namespace PersonalWorkbench.Services;

/// <summary>
/// 将阿拉伯数字金额转换为人民币汉字大写。
/// </summary>
public sealed partial class AmountUppercaseService
{
    private const int MaxIntegerDigits = 48;
    private static readonly string[] Numerals = ["零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖"];
    private static readonly string[] SectionUnits = ["仟", "佰", "拾", ""];
    private static readonly string[] GroupUnits = ["", "万", "亿", "兆", "京", "垓", "秭", "穰", "沟", "涧", "正", "载"];

    /// <summary>
    /// 将金额转换为人民币汉字大写。小数最多支持两位，第一位为角、第二位为分。
    /// </summary>
    /// <param name="input">只包含数字、可选正负号和小数点的金额。</param>
    /// <returns>带“元”和“整”的人民币大写金额。</returns>
    public string Convert(string? input)
    {
        var normalized = input?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !AmountPattern().IsMatch(normalized))
        {
            throw new ArgumentException("请输入有效金额，例如 1234.56，最多支持两位小数。", nameof(input));
        }

        var negative = normalized[0] is '-' or '+';
        var unsigned = negative ? normalized[1..] : normalized;
        var separatorIndex = unsigned.IndexOf('.');
        var integerPart = separatorIndex >= 0 ? unsigned[..separatorIndex] : unsigned;
        var fractionPart = separatorIndex >= 0 ? unsigned[(separatorIndex + 1)..] : string.Empty;
        var significantInteger = integerPart.TrimStart('0');
        significantInteger = significantInteger.Length == 0 ? "0" : significantInteger;

        if (significantInteger.Length > MaxIntegerDigits)
        {
            throw new ArgumentException($"整数部分最多支持 {MaxIntegerDigits} 位。", nameof(input));
        }

        var fraction = fractionPart.PadRight(2, '0');
        var isZero = significantInteger == "0" && fraction == "00";
        var result = new StringBuilder();

        if (normalized[0] == '-' && !isZero)
        {
            result.Append('负');
        }

        result.Append(ConvertInteger(significantInteger));
        result.Append('元');

        if (fraction == "00")
        {
            result.Append("整");
        }
        else
        {
            if (fraction[0] != '0')
            {
                result.Append(Numerals[fraction[0] - '0']).Append('角');
            }

            if (fraction[1] != '0')
            {
                result.Append(Numerals[fraction[1] - '0']).Append('分');
            }
        }

        return result.ToString();
    }

    private static string ConvertInteger(string integer)
    {
        if (integer == "0")
        {
            return Numerals[0];
        }

        var groupCount = (integer.Length + 3) / 4;
        var padded = integer.PadLeft(groupCount * 4, '0');
        var result = new StringBuilder();
        var zeroPending = false;

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var section = padded.Substring(groupIndex * 4, 4);
            var sectionValue = int.Parse(section);

            if (sectionValue == 0)
            {
                if (result.Length > 0)
                {
                    zeroPending = true;
                }

                continue;
            }

            // 非最高位组不足四位时，组间需要补一个“零”。
            if (result.Length > 0 && (zeroPending || sectionValue < 1000))
            {
                result.Append('零');
            }

            result.Append(ConvertSection(section));
            result.Append(GroupUnits[groupCount - groupIndex - 1]);
            zeroPending = false;
        }

        return result.ToString();
    }

    private static string ConvertSection(string section)
    {
        var result = new StringBuilder();
        var zeroPending = false;

        for (var index = 0; index < section.Length; index++)
        {
            var digit = section[index] - '0';
            if (digit == 0)
            {
                if (result.Length > 0)
                {
                    zeroPending = true;
                }

                continue;
            }

            if (zeroPending)
            {
                result.Append('零');
            }

            result.Append(Numerals[digit]).Append(SectionUnits[index]);
            zeroPending = false;
        }

        return result.ToString();
    }

    [GeneratedRegex(@"^[+-]?[0-9]+(?:\.[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();
}
