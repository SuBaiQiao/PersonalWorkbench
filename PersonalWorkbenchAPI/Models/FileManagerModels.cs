namespace PersonalWorkbench.Models;

/// <summary>磁盘入口；配置了限定目录时，入口也可以是普通文件夹。</summary>
public sealed record FileManagerRoot(string Name, string Path, long? TotalBytes, long? FreeBytes);
public sealed record FileManagerCapabilities(string MachineName, bool ReadOnly, int MaxTextBytes, IReadOnlyList<FileManagerRoot> Roots);

/// <summary>不递归统计目录大小，避免浏览大型磁盘时长时间阻塞。</summary>
public sealed record DiskEntry(string Name, string Path, bool IsDirectory, long? Size,
    DateTime CreatedUtc, DateTime ModifiedUtc, string Extension, string Attributes,
    bool IsReadOnly, bool IsLink, string PreviewKind);
public sealed record DirectoryPage(string Path, string? ParentPath, IReadOnlyList<DiskEntry> Entries,
    int Total, int Offset, int Limit, int Skipped);
public sealed record FileText(string Path, string Content, string Encoding, bool HasBom, string Version, bool IsReadOnly);
public sealed record CreateDiskEntryRequest(string ParentPath, string Name, bool IsDirectory);
public sealed record SaveFileTextRequest(string Path, string Content, string Version, string Encoding, bool HasBom);
