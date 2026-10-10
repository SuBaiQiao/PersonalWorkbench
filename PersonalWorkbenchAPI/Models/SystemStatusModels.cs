namespace PersonalWorkbench.Models;

public sealed record SystemStatusResponse(
    DateTimeOffset CollectedAt,
    string MachineName,
    string OperatingSystem,
    long UptimeSeconds,
    CpuStatus Cpu,
    MemoryStatus Memory,
    GpuStatus Gpu,
    IReadOnlyList<DiskStatus> Disks,
    IReadOnlyList<string> Warnings);

public sealed record CpuStatus(string? Name, int? PhysicalCores, int LogicalProcessors, double? UsagePercent);

public sealed record MemoryStatus(long? TotalBytes, long? AvailableBytes, long? UsedBytes, double? UsagePercent);

public sealed record GpuStatus(
    IReadOnlyList<GpuAdapterStatus> Adapters,
    double? UsagePercent,
    long? DedicatedUsedBytes,
    long? SharedUsedBytes,
    string? Note);

public sealed record GpuAdapterStatus(
    string Id,
    string Name,
    string? DriverVersion,
    double? UsagePercent,
    long? TotalMemoryBytes,
    long? UsedMemoryBytes,
    double? TemperatureCelsius);

public sealed record DiskStatus(
    string Name,
    string? Label,
    string? FileSystem,
    string DriveType,
    bool IsReady,
    long? TotalBytes,
    long? UsedBytes,
    long? FreeBytes,
    double? UsagePercent,
    string? Error);
