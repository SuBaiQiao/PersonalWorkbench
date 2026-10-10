using System.Text.RegularExpressions;

namespace PersonalWorkbench.Services;

public sealed partial class SqlCaseConverterService
{
    /// <summary>
    /// 将文本中的驼峰命名转换为大写下划线命名，并规范空白字符。
    /// </summary>
    /// <param name="input">待转换的 SQL 或命名片段。</param>
    /// <returns>转换后的文本；输入为空时返回空字符串。</returns>
    public string Convert(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // 先统一换行、制表符和连续空格，避免输出格式不稳定。
        var normalizedWhitespace = WhitespaceRegex().Replace(input.Trim(), " ");
        return CamelCaseRegex().Replace(normalizedWhitespace, "$1_$2").ToUpperInvariant();
    }

    /// <summary>
    /// 创建用于匹配连续空白字符的正则表达式。
    /// </summary>
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// 创建用于匹配小写字母或数字后紧跟大写字母的正则表达式。
    /// </summary>
    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelCaseRegex();
}
