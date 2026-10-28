using System.ComponentModel.DataAnnotations;

namespace PersonalWorkbench.Models;

public sealed record TempCleanupAutomationRequest([Required] bool? Enabled, [Required, MaxLength(4096)] string ExpectedTempPath);
public sealed record TempCleanupCleanRequest([Required, MaxLength(4096)] string ExpectedTempPath);

/// <summary>统一表示预览与执行结果；FreedBytes 为成功删除文件的逻辑大小，running 表示未记录完成。</summary>
public sealed record TempCleanupResult(string TempPath, DateTime StartedUtc, DateTime? CompletedUtc,
    string Trigger, string State, int Inspected, int Files, int Folders, long Bytes,
    int DeletedFiles, int DeletedFolders, long FreedBytes, int Skipped, bool Limited, IReadOnlyList<string> Errors);

public sealed record TempCleanupStatus
{
    public bool Supported { get; init; }
    public string? Reason { get; init; }
    public string MachineName { get; init; } = Environment.MachineName;
    public string Account { get; init; } = Environment.UserName;
    public string? TempPath { get; init; }
    public string? StateDirectory { get; init; }
    public bool AutomationSupported { get; init; }
    public bool AutoEnabled { get; init; }
    public bool CanManage { get; init; }
    public string? TaskName { get; init; }
    public string? TaskState { get; init; }
    public DateTime? LastTaskRunUtc { get; init; }
    public long? LastTaskResult { get; init; }
    public TempCleanupResult? LastCleanup { get; init; }
}
