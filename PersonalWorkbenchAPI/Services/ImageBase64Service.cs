using System.Text;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed class ImageBase64Service
{
    /// <summary>
    /// 限制单张图片的处理大小，避免将异常大的内容直接读入内存。
    /// </summary>
    public const long MaxImageBytes = 20 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> ContentTypeExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/gif"] = ".gif",
            ["image/webp"] = ".webp",
            ["image/bmp"] = ".bmp",
            ["image/svg+xml"] = ".svg"
        };

    private static readonly IReadOnlyDictionary<string, string> FileExtensionsContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".bmp"] = "image/bmp",
            [".svg"] = "image/svg+xml"
        };

    /// <summary>
    /// 将 Base64 或 Data URL 解码为图片字节，并推断下载文件名和媒体类型。
    /// </summary>
    /// <param name="request">Base64 内容和可选文件名。</param>
    /// <returns>图片字节、媒体类型和下载文件名。</returns>
    /// <exception cref="ArgumentException">输入为空、格式错误或图片格式不受支持时抛出。</exception>
    public (byte[] Bytes, string ContentType, string FileName) Decode(Base64ToImageRequest request)
    {
        var input = request.Base64?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("请输入 Base64 图片内容。", nameof(request));
        }

        var payload = input;
        string? declaredContentType = null;
        if (input.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            (payload, declaredContentType) = ParseDataUrl(input);
        }

        // Base64 文本中允许存在换行和空格，解码前将它们统一移除。
        payload = string.Concat(payload.Where(character => !char.IsWhiteSpace(character)));

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Base64 内容格式不正确。", nameof(request), exception);
        }

        ValidateSize(bytes.LongLength);
        var contentType = ResolveContentType(bytes, declaredContentType, request.FileName);
        var fileName = BuildFileName(request.FileName, contentType);
        return (bytes, contentType, fileName);
    }

    /// <summary>
    /// 将上传的图片流编码为纯 Base64 和可直接用于浏览器预览的 Data URL。
    /// </summary>
    /// <param name="content">图片输入流。</param>
    /// <param name="fileName">上传文件名，用于推断类型和保留输出名称。</param>
    /// <param name="contentType">浏览器提供的媒体类型。</param>
    /// <param name="length">上传文件长度。</param>
    /// <returns>图片文件名、媒体类型、大小、纯 Base64 和 Data URL。</returns>
    /// <exception cref="ArgumentException">输入为空、格式错误或图片格式不受支持时抛出。</exception>
    public ImageBase64Response Encode(Stream content, string fileName, string? contentType, long length)
    {
        if (content is null || length <= 0)
        {
            throw new ArgumentException("请选择一张图片。", nameof(content));
        }

        ValidateSize(length);
        using var memory = new MemoryStream();
        content.CopyTo(memory);
        var bytes = memory.ToArray();
        ValidateSize(bytes.LongLength);

        var resolvedContentType = ResolveContentType(bytes, contentType, fileName);
        var base64 = Convert.ToBase64String(bytes);
        return new ImageBase64Response(
            Path.GetFileName(fileName),
            resolvedContentType,
            bytes.LongLength,
            base64,
            $"data:{resolvedContentType};base64,{base64}");
    }

    /// <summary>
    /// 解析 Data URL 的媒体类型和 Base64 负载。
    /// </summary>
    /// <param name="dataUrl">以 data: 开头的图片 Data URL。</param>
    /// <returns>Base64 负载和声明的媒体类型。</returns>
    private static (string Payload, string ContentType) ParseDataUrl(string dataUrl)
    {
        var separatorIndex = dataUrl.IndexOf(',');
        if (separatorIndex < 0)
        {
            throw new ArgumentException("Data URL 缺少 Base64 内容。", nameof(dataUrl));
        }

        var metadata = dataUrl[5..separatorIndex];
        var parts = metadata.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !parts.Skip(1).Any(part => part.Equals("base64", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("只支持 Base64 格式的 Data URL。", nameof(dataUrl));
        }

        return (dataUrl[(separatorIndex + 1)..], parts[0]);
    }

    /// <summary>
    /// 从媒体类型、文件扩展名和文件头依次推断图片类型。
    /// </summary>
    /// <param name="bytes">图片字节。</param>
    /// <param name="declaredContentType">请求声明的媒体类型。</param>
    /// <param name="fileName">原始文件名。</param>
    /// <returns>支持的图片媒体类型。</returns>
    private static string ResolveContentType(byte[] bytes, string? declaredContentType, string? fileName)
    {
        var normalizedType = declaredContentType?.Split(';', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(normalizedType)
            && ContentTypeExtensions.ContainsKey(normalizedType))
        {
            return normalizedType;
        }

        var detectedType = DetectContentType(bytes);
        if (detectedType is not null)
        {
            return detectedType;
        }

        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (FileExtensionsContentTypes.TryGetValue(extension, out var extensionType))
        {
            return extensionType;
        }

        throw new ArgumentException("无法识别图片格式，仅支持 PNG、JPG、GIF、WEBP、BMP 和 SVG 图片。");
    }

    /// <summary>
    /// 根据常见图片文件头识别媒体类型。
    /// </summary>
    /// <param name="bytes">待识别的图片字节。</param>
    /// <returns>识别出的媒体类型，无法识别时返回 null。</returns>
    private static string? DetectContentType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 255, 216, 255 }))
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 6 && Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a")
        {
            return "image/gif";
        }

        if (bytes.Length >= 12
            && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF"
            && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
        {
            return "image/webp";
        }

        if (bytes.Length >= 2 && bytes.AsSpan(0, 2).SequenceEqual(new byte[] { 66, 77 }))
        {
            return "image/bmp";
        }

        var text = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        return text.Contains("<svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : null;
    }

    /// <summary>
    /// 校验图片大小是否在服务允许范围内。
    /// </summary>
    /// <param name="length">图片字节数。</param>
    private static void ValidateSize(long length)
    {
        if (length > MaxImageBytes)
        {
            throw new ArgumentException($"图片不能超过 {MaxImageBytes / 1024 / 1024} MB。");
        }
    }

    /// <summary>
    /// 根据用户提供的文件名生成安全且扩展名正确的下载文件名。
    /// </summary>
    /// <param name="fileName">用户提供的文件名。</param>
    /// <param name="contentType">图片媒体类型。</param>
    /// <returns>安全的图片文件名。</returns>
    private static string BuildFileName(string? fileName, string contentType)
    {
        var extension = ContentTypeExtensions[contentType];
        var safeName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return $"converted-image{extension}";
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
        return $"{nameWithoutExtension}{extension}";
    }
}
