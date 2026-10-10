namespace PersonalWorkbench.Models;

public sealed record CpuDetailsResponse(
    DateTimeOffset CollectedAt,
    string Architecture,
    IReadOnlyList<CpuPackageDetails> Packages,
    IReadOnlyList<LogicalProcessorUsage> LogicalProcessors,
    IReadOnlyList<string> Warnings);

public sealed record CpuPackageDetails(
    string Id,
    string Name,
    string? Manufacturer,
    string? Socket,
    int? PhysicalCores,
    int? LogicalProcessors,
    double? ReportedClockMhz,
    double? MaxClockMhz,
    long? L2CacheBytes,
    long? L3CacheBytes,
    bool? VirtualizationFirmwareEnabled);

public sealed record LogicalProcessorUsage(string Id, double? UsagePercent);
