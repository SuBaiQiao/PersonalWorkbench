using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>
/// 管理 API 所在机器的文件。所有入口复用路径校验，拒绝设备路径、数据流和符号链接；
/// Roots 可把可访问范围收窄到指定目录，ReadOnly 可独立禁用写入。
/// </summary>
public sealed class FileManagerService
{
    private readonly string[] configuredRoots;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public bool ReadOnly { get; }
    public int MaxTextBytes { get; }

    public FileManagerService(IConfiguration configuration)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        configuredRoots = (configuration.GetSection("FileManager:Roots").Get<string[]>() ?? [])
            .Where(root => !string.IsNullOrWhiteSpace(root)).Select(Normalize).Distinct().ToArray();
        ReadOnly = configuration.GetValue("FileManager:ReadOnly", false);
        MaxTextBytes = Math.Clamp(configuration.GetValue("FileManager:MaxTextBytes", 2 * 1024 * 1024), 1024, 10 * 1024 * 1024);
    }

    /// <summary>每次枚举已就绪磁盘，允许运行期间插入移动硬盘；无法读取容量时仍保留入口。</summary>
    public FileManagerCapabilities GetCapabilities()
    {
        var roots = GetRootPaths().Select(path =>
        {
            long? total = null, free = null;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path)!);
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
            return new FileManagerRoot(path, path, total, free);
        }).ToArray();
        return new(Environment.MachineName, ReadOnly, MaxTextBytes, roots);
    }

    private string[] GetRootPaths() => configuredRoots.Length > 0 ? configuredRoots : DriveInfo.GetDrives()
        .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram)
        .Select(drive => Normalize(drive.RootDirectory.FullName)).ToArray();

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private bool IsWithin(string path, string root) => path.Equals(root, pathComparison)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, pathComparison);

    /// <summary>逐层检查重解析点，避免通过 junction/symlink 绕过限定目录，或在写入时跟随链接。</summary>
    public string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("请输入完整的磁盘路径。");
        if (OperatingSystem.IsWindows() && (path.StartsWith(@"\\") || path.IndexOf(':', 2) >= 0))
            throw new ArgumentException("不支持网络共享、设备路径或 NTFS 数据流。");
        var fullPath = Normalize(path);
        if (!GetRootPaths().Any(root => IsWithin(fullPath, root)))
            throw new UnauthorizedAccessException("该路径不在允许访问的磁盘或目录内。");
        var current = Path.GetPathRoot(fullPath)!;
        // 从磁盘根开始检查，配置目录本身也不能借助链接跳转到其他位置。
        var segments = fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("暂不进入符号链接或目录联接，请使用实际磁盘路径。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return fullPath;
    }

    public DirectoryPage List(string path, string? search, bool showHidden, string sort, bool descending, int offset, int limit, CancellationToken cancellationToken)
    {
        path = ValidatePath(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException();
        search = (search ?? "").Trim();
        if (search.Length > 200) throw new ArgumentException("搜索内容不能超过 200 个字符。");
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 200);
        var entries = new List<DiskEntry>();
        var skipped = 0;
        // 仅枚举当前一层。某个文件权限不足或在枚举时消失，不影响其他文件的展示。
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!showHidden && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (!info.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(ToEntry(info));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { skipped++; }
        }
        Func<DiskEntry, object> key = sort switch
        {
            "size" => entry => entry.Size ?? -1,
            "modified" => entry => entry.ModifiedUtc,
            "type" => entry => entry.Extension,
            _ => entry => entry.Name
        };
        var foldersFirst = entries.OrderByDescending(entry => entry.IsDirectory);
        var sorted = descending ? foldersFirst.ThenByDescending(key) : foldersFirst.ThenBy(key);
        var parent = Directory.GetParent(path)?.FullName;
        if (parent != null && !GetRootPaths().Any(root => IsWithin(parent, root))) parent = null;
        return new(path, parent, sorted.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(offset).Take(limit).ToArray(), entries.Count, offset, limit, skipped);
    }

    public DiskEntry GetEntry(string path)
    {
        path = ValidatePath(path);
        var attributes = File.GetAttributes(path);
        return ToEntry((attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path));
    }

    private DiskEntry ToEntry(FileSystemInfo info)
    {
        var attributes = info.Attributes;
        var directory = (attributes & FileAttributes.Directory) != 0;
        var link = (attributes & FileAttributes.ReparsePoint) != 0;
        var extension = directory ? "" : info.Extension.ToLowerInvariant();
        var size = info is FileInfo file && !link ? file.Length : (long?)null;
        return new(info.Name, info.FullName, directory, size, info.CreationTimeUtc, info.LastWriteTimeUtc,
            extension, attributes.ToString(), ReadOnly || (!directory && (attributes & FileAttributes.ReadOnly) != 0), link,
            link || directory ? "none" : PreviewType(extension, size));
    }

    private string PreviewType(string extension, long? size) => extension switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".avif" or ".ico" => "image",
        ".pdf" => "pdf",
        ".mp4" or ".webm" or ".ogv" or ".mov" or ".m4v" => "video",
        ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" or ".aac" => "audio",
        ".zip" or ".7z" or ".rar" or ".exe" or ".dll" or ".bin" or ".db" or ".sqlite" or ".sqlite3"
            or ".doc" or ".xls" or ".ppt" or ".docx" or ".xlsx" or ".pptx" or ".iso" or ".woff" or ".woff2" or ".ttf" => "download",
        _ => size <= MaxTextBytes ? "text" : "download"
    };

    public async Task<DiskEntry> CreateAsync(CreateDiskEntryRequest request, CancellationToken cancellationToken)
    {
        EnsureWritable();
        var parent = ValidatePath(request.ParentPath);
        ValidateName(request.Name);
        var target = ValidatePath(Path.Combine(parent, request.Name));
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            ValidatePath(target);
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException();
            if (File.Exists(target) || Directory.Exists(target)) throw new FileManagerConflictException("已存在同名文件或文件夹。");
            if (request.IsDirectory) Directory.CreateDirectory(target);
            else
            {
                await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            return GetEntry(target);
        }
        finally { writeGate.Release(); }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\')
            || name.Any(char.IsControl) || name.EndsWith('.') || name.EndsWith(' ')
            || (OperatingSystem.IsWindows() && Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase)))
            throw new ArgumentException("名称无效，请勿包含路径分隔符、保留名称或结尾的空格和句点。");
    }

    public async Task<FileText> ReadTextAsync(string path, string? encodingName, CancellationToken cancellationToken)
    {
        path = ValidatePath(path);
        var bytes = await ReadBytesAsync(path, cancellationToken);
        var (encoding, preambleLength) = DetectEncoding(bytes, encodingName);
        string content;
        try { content = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength); }
        catch (DecoderFallbackException) { throw new FileManagerFormatException("不是有效的文本编码，请选择 GB18030 重试，或下载原文件。"); }
        if (HasBinaryCharacters(content))
            throw new FileManagerFormatException("文件包含二进制内容，不能作为文本在线编辑，请下载后打开。");
        return new(path, content, encoding.WebName.ToLowerInvariant(), preambleLength > 0, Version(bytes), GetEntry(path).IsReadOnly);
    }

    /// <summary>按实际字节读取上限，而非只相信读取前的文件长度，防止外部程序持续追加。</summary>
    private async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length > MaxTextBytes) throw new FileManagerFormatException($"在线编辑仅支持不超过 {MaxTextBytes / 1024} KB 的文本文件。");
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > MaxTextBytes) throw new FileManagerFormatException("文本文件过大，请下载后编辑。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static string Version(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool HasBinaryCharacters(string content) => content.Any(character => char.IsControl(character)
        && character is not '\r' and not '\n' and not '\t' and not '\f');
    private static Encoding GetEncoding(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "utf-8" => new UTF8Encoding(false, true),
        "utf-16" => new UnicodeEncoding(false, false, true),
        "utf-16be" => new UnicodeEncoding(true, false, true),
        "utf-32" => new UTF32Encoding(false, false, true),
        "utf-32be" => new UTF32Encoding(true, false, true),
        "gb18030" => Encoding.GetEncoding("gb18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => throw new ArgumentException("不支持该文本编码。")
    };

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes, string? name)
    {
        // UTF-32 的 BOM 与 UTF-16 有重叠，必须优先匹配较长的签名。
        foreach (var encoding in new Encoding[] { new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
            new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
        {
            var bom = encoding.GetPreamble();
            if (bytes.AsSpan().StartsWith(bom)) return (encoding, bom.Length);
        }
        return (GetEncoding(name), 0);
    }

    public async Task<FileText> SaveTextAsync(SaveFileTextRequest request, CancellationToken cancellationToken)
    {
        EnsureWritable();
        var path = ValidatePath(request.Path);
        if (string.IsNullOrEmpty(request.Version)) throw new ArgumentException("缺少原始文件版本，请重新打开文件。");
        if (request.Content == null || request.Content.Length > MaxTextBytes) throw new FileManagerFormatException("编辑内容过大。");
        if (HasBinaryCharacters(request.Content)) throw new FileManagerFormatException("内容包含二进制控制字符，不能作为文本保存。");
        var encoding = GetEncoding(request.Encoding);
        byte[] body;
        try { body = encoding.GetBytes(request.Content); }
        catch (EncoderFallbackException) { throw new FileManagerFormatException("所选编码无法保存部分字符，请改用 UTF-8。"); }
        var bom = request.HasBom ? encoding.CodePage switch
        {
            65001 => new byte[] { 0xEF, 0xBB, 0xBF }, 1200 => [0xFF, 0xFE], 1201 => [0xFE, 0xFF],
            12000 => [0xFF, 0xFE, 0, 0], 12001 => [0, 0, 0xFE, 0xFF], _ => []
        } : Array.Empty<byte>();
        var bytes = bom.Concat(body).ToArray();
        if (bytes.Length > MaxTextBytes) throw new FileManagerFormatException("保存后的文件超过在线编辑大小上限。");
        await writeGate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            if (GetEntry(path).IsReadOnly) throw new UnauthorizedAccessException("该文件为只读，无法保存。");
            // 目标编码可能与磁盘原编码不同，版本检查必须针对原字节，不能按目标编码重新解码原文件。
            if (Version(await ReadBytesAsync(path, cancellationToken)) != request.Version)
                throw new FileManagerConflictException("文件已被其他程序或页面修改。请先复制当前内容，再重新加载文件进行比较。");
            // 同目录写入完整临时文件后再替换，避免磁盘写入失败把原文件截断；不跨卷移动。
            temporary = ValidatePath(Path.Combine(Path.GetDirectoryName(path)!, $".workbench-{Guid.NewGuid():N}.tmp"));
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            if (Version(await ReadBytesAsync(path, cancellationToken)) != request.Version)
                throw new FileManagerConflictException("保存前检测到文件发生变化，请重新加载后比较。");
            ValidatePath(path);
            File.Replace(temporary, path, null);
            temporary = null;
            return new(path, request.Content, encoding.WebName.ToLowerInvariant(), bom.Length > 0, Version(bytes), GetEntry(path).IsReadOnly);
        }
        finally
        {
            // 只清理本次创建且经过目录边界校验的临时文件。
            try { if (temporary != null && File.Exists(temporary)) File.Delete(ValidatePath(temporary)); }
            finally { writeGate.Release(); }
        }
    }

    public (FileStream Stream, string ContentType, string Name, bool Inline) OpenContent(string path, bool download)
    {
        var entry = GetEntry(path);
        if (entry.IsDirectory) throw new ArgumentException("文件夹不能直接预览或下载。");
        var mime = entry.Extension switch
        {
            ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp",
            ".bmp" => "image/bmp", ".avif" => "image/avif", ".ico" => "image/x-icon", ".pdf" => "application/pdf",
            ".mp4" or ".m4v" => "video/mp4", ".mov" => "video/quicktime", ".webm" => "video/webm", ".ogv" => "video/ogg",
            ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".ogg" => "audio/ogg", ".m4a" => "audio/mp4",
            ".flac" => "audio/flac", ".aac" => "audio/aac", _ => "application/octet-stream"
        };
        // HTML、SVG 和脚本只允许作为源码编辑或附件下载，绝不作为同源网页执行。
        return (new FileStream(ValidatePath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true), mime, entry.Name,
            !download && entry.PreviewKind is "image" or "pdf" or "video" or "audio");
    }

    private void EnsureWritable()
    {
        if (ReadOnly) throw new UnauthorizedAccessException("管理员已将文件管理设置为只读模式。");
    }
}

public sealed class FileManagerConflictException(string message) : Exception(message);
public sealed class FileManagerFormatException(string message) : Exception(message);
