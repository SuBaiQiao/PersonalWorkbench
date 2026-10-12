namespace PersonalWorkbench.Models;

public sealed record GpuDetailsResponse(
    DateTimeOffset CollectedAt,
    IReadOnlyList<GpuDetailedAdapter> Adapters,
    IReadOnlyList<GpuEngineUsage> Engines,
    IReadOnlyList<GpuMemoryCounter> MemoryCounters,
    IReadOnlyList<GpuProcessMemoryUsage> ProcessMemory,
    IReadOnlyList<string> Warnings);

public sealed record GpuDetailedAdapter(
    string Id,
    string Name,
    string? DriverVersion,
    string? PciBusId,
    string? VbiosVersion,
    string? PerformanceState,
    string? ComputeMode,
    double? GpuUsagePercent,
    double? MemoryUsagePercent,
    long? TotalMemoryBytes,
    long? UsedMemoryBytes,
    long? FreeMemoryBytes,
    double? TemperatureCelsius,
    double? MemoryTemperatureCelsius,
    double? FanPercent,
    double? PowerDrawWatts,
    double? PowerLimitWatts,
    int? GraphicsClockMhz,
    int? MemoryClockMhz,
    int? MaxGraphicsClockMhz,
    int? MaxMemoryClockMhz,
    double? EncoderUsagePercent,
    double? DecoderUsagePercent,
    string? EccMode,
    string? ClockThrottleReasons,
    int? PciLinkGeneration,
    int? PciLinkWidth,
    IReadOnlyList<GpuProcessUsage> Processes);

public sealed record GpuProcessUsage(int ProcessId, string? Name, long? UsedMemoryBytes);

public sealed record GpuEngineUsage(
    string AdapterInstance,
    int? ProcessId,
    string EngineType,
    double? UsagePercent);

public sealed record GpuMemoryCounter(string AdapterInstance, long? DedicatedBytes, long? SharedBytes);

public sealed record GpuProcessMemoryUsage(int ProcessId, long? DedicatedBytes, long? SharedBytes);

public sealed record ProcessListResponse(
    DateTimeOffset CollectedAt,
    int SampleIntervalMilliseconds,
    int ProcessorCount,
    bool NetworkBandwidthAvailable,
    string NetworkNote,
    int? ActiveTcpConnections,
    int? ActiveUdpEndpoints,
    IReadOnlyList<ProcessUsage> Processes,
    IReadOnlyList<string> Warnings);

public sealed record ProcessUsage(
    int ProcessId,
    string Name,
    string? FilePath,
    DateTimeOffset? StartedAt,
    int? ParentProcessId,
    int? SessionId,
    double? CpuPercent,
    long? WorkingSetBytes,
    long? PrivateMemoryBytes,
    long? VirtualMemoryBytes,
    long? PagedMemoryBytes,
    long? ReadBytesPerSecond,
    long? WriteBytesPerSecond,
    long? NetworkReceivedBytesPerSecond,
    long? NetworkSentBytesPerSecond,
    int? ThreadCount,
    int? HandleCount,
    int? BasePriority,
    double? GpuPercent,
    long? GpuMemoryBytes,
    string? Status,
    string? Error,
    int? TcpConnections,
    int? UdpEndpoints);
