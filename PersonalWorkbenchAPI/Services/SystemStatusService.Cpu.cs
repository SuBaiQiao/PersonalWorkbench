using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed partial class SystemStatusService
{
    private readonly SemaphoreSlim _cpuDetailsLock = new(1, 1);
    private CpuDetailsResponse? _cpuDetails;

    public async Task<CpuDetailsResponse> GetCpuDetailsAsync(CancellationToken cancellationToken)
    {
        await _cpuDetailsLock.WaitAsync(cancellationToken);
        try
        {
            if (_cpuDetails is not null && DateTimeOffset.UtcNow - _cpuDetails.CollectedAt < TimeSpan.FromSeconds(3))
                return _cpuDetails;

            var warnings = new List<string>();
            IReadOnlyList<CpuPackageDetails> packages = [];
            IReadOnlyList<LogicalProcessorUsage> processors = [];
            if (OperatingSystem.IsWindows())
            {
                const string script = """
                    $warnings = @()
                    $packages = @()
                    $processors = @()
                    try {
                        $packages = @(Get-CimInstance Win32_Processor -ErrorAction Stop | ForEach-Object {
                            @{
                                Id = $_.DeviceID; Name = $_.Name; Manufacturer = $_.Manufacturer; Socket = $_.SocketDesignation
                                PhysicalCores = if ($_.NumberOfCores -gt 0) { [int]$_.NumberOfCores } else { $null }
                                LogicalProcessors = if ($_.NumberOfLogicalProcessors -gt 0) { [int]$_.NumberOfLogicalProcessors } else { $null }
                                ReportedClockMhz = if ($_.CurrentClockSpeed -gt 0) { [double]$_.CurrentClockSpeed } else { $null }
                                MaxClockMhz = if ($_.MaxClockSpeed -gt 0) { [double]$_.MaxClockSpeed } else { $null }
                                L2CacheBytes = if ($_.L2CacheSize -gt 0) { [long]$_.L2CacheSize * 1024 } else { $null }
                                L3CacheBytes = if ($_.L3CacheSize -gt 0) { [long]$_.L3CacheSize * 1024 } else { $null }
                                VirtualizationFirmwareEnabled = $_.VirtualizationFirmwareEnabled
                            }
                        })
                    } catch { $warnings += 'CPU 硬件详情读取失败，请确认系统 CIM 服务可用。' }
                    try {
                        $first = @{}
                        Get-CimInstance Win32_PerfRawData_PerfOS_Processor -ErrorAction Stop | ForEach-Object { $first[$_.Name] = $_ }
                        Start-Sleep -Milliseconds 500
                        $processors = @(Get-CimInstance Win32_PerfRawData_PerfOS_Processor -ErrorAction Stop | Where-Object { $_.Name -ne '_Total' } | ForEach-Object {
                            $previous = $first[$_.Name]
                            $elapsed = [double]$_.Timestamp_Sys100NS - [double]$previous.Timestamp_Sys100NS
                            $idle = [double]$_.PercentProcessorTime - [double]$previous.PercentProcessorTime
                            @{
                                Id = $_.Name
                                UsagePercent = if ($null -ne $previous -and $elapsed -gt 0 -and $idle -ge 0) {
                                    [Math]::Round([Math]::Min(100, [Math]::Max(0, 100 * (1 - $idle / $elapsed))), 1)
                                } else { $null }
                            }
                        })
                    } catch { $warnings += '各逻辑处理器使用率读取失败，请确认系统性能计数器可用。' }
                    @{ Packages = $packages; LogicalProcessors = $processors; Warnings = $warnings } | ConvertTo-Json -Depth 5 -Compress
                    """;
                var output = await RunPowerShellAsync(script, cancellationToken, TimeSpan.FromSeconds(10));
                try
                {
                    var data = output is null ? null : JsonSerializer.Deserialize<WindowsCpuDetails>(output, JsonOptions);
                    if (data is null) warnings.Add("CPU 详情读取失败或超时。");
                    else { packages = data.Packages; processors = data.LogicalProcessors; warnings.AddRange(data.Warnings); }
                }
                catch (JsonException exception) { logger.LogDebug(exception, "解析 CPU 详情失败"); warnings.Add("CPU 详情解析失败。"); }
            }
            else if (OperatingSystem.IsLinux())
            {
                try
                {
                    var blocks = File.ReadAllText("/proc/cpuinfo").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                        .Select(block => block.Split('\n').Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2)
                            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim()))
                        .Where(block => block.ContainsKey("processor")).ToArray();
                    packages = blocks.GroupBy(block => block.GetValueOrDefault("physical id", "0"))
                        .Select(group =>
                        {
                            var first = group.First();
                            var coreIds = group.Where(block => block.ContainsKey("core id")).Select(block => block["core id"]).Distinct().Count();
                            double? reported = Number(first.GetValueOrDefault("cpu MHz", ""));
                            double? max = null;
                            var path = $"/sys/devices/system/cpu/cpu{first["processor"]}/cpufreq/cpuinfo_max_freq";
                            if (File.Exists(path)) max = Number(File.ReadAllText(path).Trim()) / 1000;
                            return new CpuPackageDetails(group.Key, first.GetValueOrDefault("model name", "CPU"),
                                first.GetValueOrDefault("vendor_id"), group.Key, coreIds > 0 ? coreIds : null,
                                group.Count(), reported, max, null, null, null);
                        }).ToArray();
                    warnings.Add("Linux 暂不提供缓存容量和固件虚拟化状态。");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
                { logger.LogDebug(exception, "读取 Linux CPU 详情失败"); warnings.Add("CPU 硬件详情读取失败。"); }

                try
                {
                    var first = ReadLinuxProcessorTimes();
                    await Task.Delay(500, cancellationToken);
                    processors = ReadLinuxProcessorTimes().Select(pair =>
                    {
                        double? usage = null;
                        if (first.TryGetValue(pair.Key, out var before) && pair.Value.Total > before.Total && pair.Value.Idle >= before.Idle)
                            usage = Math.Round(Math.Clamp(100d * (1 - (double)(pair.Value.Idle - before.Idle) / (pair.Value.Total - before.Total)), 0, 100), 1);
                        return new LogicalProcessorUsage(pair.Key, usage);
                    }).ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
                { logger.LogDebug(exception, "读取 Linux CPU 使用率失败"); warnings.Add("各逻辑处理器使用率读取失败。"); }
            }
            else warnings.Add("当前系统暂不支持 CPU 详情读取。");

            _cpuDetails = new CpuDetailsResponse(DateTimeOffset.UtcNow, RuntimeInformation.OSArchitecture.ToString(), packages,
                processors.OrderBy(item => int.TryParse(item.Id, out var index) ? index : int.MaxValue).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray(), warnings);
            return _cpuDetails;
        }
        finally { _cpuDetailsLock.Release(); }
    }

    private static Dictionary<string, CpuTimes> ReadLinuxProcessorTimes() => File.ReadLines("/proc/stat")
        .Where(line => line.StartsWith("cpu", StringComparison.Ordinal) && line.Length > 3 && char.IsDigit(line[3]))
        .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .ToDictionary(fields => fields[0][3..], fields =>
        {
            var times = fields.Skip(1).Take(8).Select(value => ulong.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            return new CpuTimes(times[3] + (times.Length > 4 ? times[4] : 0), times.Aggregate(0UL, (sum, value) => sum + value));
        });

    private sealed record WindowsCpuDetails(CpuPackageDetails[] Packages, LogicalProcessorUsage[] LogicalProcessors, string[] Warnings);
}
