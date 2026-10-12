using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed partial class SystemStatusService
{
    private readonly SemaphoreSlim _gpuDetailsLock = new(1, 1);
    private GpuDetailsResponse? _gpuDetails;

    public async Task<GpuDetailsResponse> GetGpuDetailsAsync(CancellationToken cancellationToken)
    {
        await _gpuDetailsLock.WaitAsync(cancellationToken);
        try
        {
            if (_gpuDetails is not null && DateTimeOffset.UtcNow - _gpuDetails.CollectedAt < TimeSpan.FromSeconds(3))
                return _gpuDetails;

            var warnings = new List<string>();
            var adapters = new List<GpuDetailedAdapter>();
            var engines = new List<GpuEngineUsage>();
            var memoryCounters = new List<GpuMemoryCounter>();
            var processMemory = new List<GpuProcessMemoryUsage>();

            var xml = await RunCommandAsync("nvidia-smi", ["-q", "-x"], TimeSpan.FromSeconds(5), cancellationToken);
            if (!string.IsNullOrWhiteSpace(xml))
            {
                try { adapters.AddRange(ParseNvidiaGpuXml(xml)); }
                catch (Exception exception) when (exception is System.Xml.XmlException or FormatException or OverflowException)
                { logger.LogDebug(exception, "解析 NVIDIA 显卡详情失败"); warnings.Add("NVIDIA 显卡详情解析失败。"); }
            }

            if (OperatingSystem.IsWindows())
            {
                if (_hardware is null || DateTimeOffset.UtcNow - _hardwareUpdatedAt > TimeSpan.FromMinutes(5))
                {
                    _hardware = await ReadWindowsHardwareAsync(cancellationToken);
                    _hardwareUpdatedAt = DateTimeOffset.UtcNow;
                }

                foreach (var adapter in _hardware?.Adapters ?? [])
                {
                    if (adapters.Any(item => item.Name.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    adapters.Add(EmptyAdapter(adapter.Id, adapter.Name, adapter.DriverVersion));
                }

                const string script = """
                    $engines = @()
                    $memory = @()
                    $processMemory = @()
                    $warnings = @()
                    try {
                        $engines = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop | ForEach-Object {
                            $processId = $null; if ($_.Name -match 'pid_(\d+)_') { $processId = [int]$Matches[1] }
                            $type = 'Unknown'; if ($_.Name -match 'engtype_(.+)$') { $type = $Matches[1] }
                            $adapter = $_.Name -replace '^.*?(luid_[^_]+_[^_]+_phys_\d+).*$', '$1'
                            @{ AdapterInstance = $adapter; ProcessId = $processId; EngineType = $type; UsagePercent = [double]$_.UtilizationPercentage }
                        })
                    } catch { $warnings += 'GPU 引擎性能计数器不可用。' }
                    try {
                        $memory = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory -ErrorAction Stop | ForEach-Object {
                            @{ AdapterInstance = $_.Name; DedicatedBytes = [long]$_.DedicatedUsage; SharedBytes = [long]$_.SharedUsage }
                        })
                    } catch { $warnings += 'GPU 显存性能计数器不可用。' }
                    try {
                        $processMemory = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -ErrorAction Stop | ForEach-Object {
                            $processId = $null; if ($_.Name -match 'pid_(\d+)_') { $processId = [int]$Matches[1] }
                            @{ ProcessId = $processId; DedicatedBytes = [long]$_.DedicatedUsage; SharedBytes = [long]$_.SharedUsage }
                        })
                    } catch { $warnings += '按进程统计的 GPU 显存计数器不可用。' }
                    @{ Engines = $engines; Memory = $memory; ProcessMemory = $processMemory; Warnings = $warnings } | ConvertTo-Json -Depth 5 -Compress
                    """;
                var output = await RunPowerShellAsync(script, cancellationToken, TimeSpan.FromSeconds(15));
                try
                {
                    var data = output is null ? null : JsonSerializer.Deserialize<WindowsGpuDetails>(output, JsonOptions);
                    if (data is null) warnings.Add("Windows GPU 性能计数器读取失败或超时。");
                    else
                    {
                        engines.AddRange(data.Engines.Where(item => item.UsagePercent is not null));
                        memoryCounters.AddRange(data.Memory);
                        processMemory.AddRange(data.ProcessMemory.Where(item => item.ProcessId > 0));
                        warnings.AddRange(data.Warnings);
                    }
                }
                catch (JsonException exception) { logger.LogDebug(exception, "解析 Windows GPU 详情失败"); warnings.Add("Windows GPU 计数器解析失败。"); }
            }

            if (adapters.Count == 0) warnings.Add("未检测到可读取的显卡详情；NVIDIA 遥测需要 nvidia-smi，Windows 其他显卡依赖系统 CIM/性能计数器。");
            if (!OperatingSystem.IsWindows() && adapters.Count == 0) warnings.Add("当前系统仅支持 nvidia-smi 提供的单卡详情。");
            if (adapters.Any(adapter => adapter.GpuUsagePercent is null) && engines.Count > 0)
                warnings.Add("Windows 引擎计数器按 GPU 实例和进程列出，系统未提供可靠的实例到显卡名称映射。");

            _gpuDetails = new GpuDetailsResponse(DateTimeOffset.UtcNow, adapters, engines, memoryCounters, processMemory, warnings);
            return _gpuDetails;
        }
        finally { _gpuDetailsLock.Release(); }
    }

    private static IReadOnlyList<GpuDetailedAdapter> ParseNvidiaGpuXml(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        return (root?.Elements("gpu") ?? []).Select(gpu =>
        {
            var memory = gpu.Element("fb_memory_usage");
            var clocks = gpu.Element("clocks");
            var maxClocks = gpu.Element("max_clocks");
            var temperature = gpu.Element("temperature");
            var utilization = gpu.Element("utilization");
            var power = gpu.Element("power_readings") ?? gpu.Element("gpu_power_readings");
            var pci = gpu.Element("pci");
            var processes = gpu.Element("processes")?.Elements("process_info") ?? [];
            var throttleReasons = gpu.Element("clocks_throttle_reasons")?.Elements()
                .Where(element => element.Name.LocalName.StartsWith("clocks_throttle_reason_", StringComparison.Ordinal)
                    && element.Name.LocalName is not "clocks_throttle_reason_supported" and not "clocks_throttle_reason_count")
                .Where(element => element.Value.Equals("Active", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Name.LocalName.Replace("clocks_throttle_reason_", "", StringComparison.Ordinal))
                .ToArray() ?? [];

            return new GpuDetailedAdapter(
                Value(gpu, "uuid") ?? Value(gpu, "pci", "pci_bus_id") ?? Value(gpu, "product_name") ?? "NVIDIA GPU",
                Value(gpu, "product_name") ?? "NVIDIA GPU", Value(root, "driver_version"), Value(gpu, "pci", "pci_bus_id"),
                Value(gpu, "vbios_version"), Value(gpu, "performance_state"), Value(gpu, "compute_mode"),
                PercentValue(Value(utilization, "gpu_util")), PercentValue(Value(utilization, "memory_util")),
                MemoryValue(Value(memory, "total")), MemoryValue(Value(memory, "used")), MemoryValue(Value(memory, "free")),
                NumericValue(Value(temperature, "gpu_temp")), NumericValue(Value(temperature, "memory_temp")),
                PercentValue(Value(gpu, "fan_speed")), NumericValue(Value(power, "power_draw")), NumericValue(Value(power, "current_power_limit") ?? Value(power, "power_limit")),
                IntegerValue(Value(clocks, "graphics_clock")), IntegerValue(Value(clocks, "mem_clock")),
                IntegerValue(Value(maxClocks, "graphics_clock")), IntegerValue(Value(maxClocks, "mem_clock")),
                PercentValue(Value(utilization, "encoder_util")), PercentValue(Value(utilization, "decoder_util")),
                Value(gpu, "ecc_mode", "current_ecc"), throttleReasons.Length == 0 ? "无活动限制" : string.Join(", ", throttleReasons),
                IntegerValue(Value(pci, "pci_gpu_link_info", "pcie_gen", "current_link_gen")),
                IntegerValue(Value(pci, "pci_gpu_link_info", "link_widths", "current_link_width")),
                processes.Select(process => new GpuProcessUsage(
                    IntegerValue(Value(process, "pid")) ?? 0, Value(process, "process_name"), MemoryValue(Value(process, "used_memory")))).ToArray());
        }).ToArray();
    }

    private static string? Value(XElement? element, params string[] path)
    {
        foreach (var name in path) element = element?.Element(name);
        var value = element?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static double? NumericValue(string? value)
    {
        if (value is null) return null;
        var number = Regex.Match(value, @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)");
        return number.Success && double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double? PercentValue(string? value) => NumericValue(value);
    private static int? IntegerValue(string? value) => NumericValue(value) is { } number && number >= int.MinValue && number <= int.MaxValue ? (int)number : null;

    private static long? MemoryValue(string? value)
    {
        if (value is null || NumericValue(value) is not { } amount) return null;
        var unit = Regex.Match(value, @"[A-Za-z]+").Value.ToUpperInvariant();
        var multiplier = unit switch { "KIB" or "KB" => 1024d, "MIB" or "MB" => 1024d * 1024, "GIB" or "GB" => 1024d * 1024 * 1024, "B" => 1d, _ => 1d };
        return amount < 0 || amount * multiplier > long.MaxValue ? null : (long)(amount * multiplier);
    }

    private static GpuDetailedAdapter EmptyAdapter(string id, string name, string? driverVersion) => new(
        Id: id, Name: name, DriverVersion: driverVersion, PciBusId: null, VbiosVersion: null,
        PerformanceState: null, ComputeMode: null, GpuUsagePercent: null, MemoryUsagePercent: null,
        TotalMemoryBytes: null, UsedMemoryBytes: null, FreeMemoryBytes: null, TemperatureCelsius: null,
        MemoryTemperatureCelsius: null, FanPercent: null, PowerDrawWatts: null, PowerLimitWatts: null,
        GraphicsClockMhz: null, MemoryClockMhz: null, MaxGraphicsClockMhz: null, MaxMemoryClockMhz: null,
        EncoderUsagePercent: null, DecoderUsagePercent: null, EccMode: null, ClockThrottleReasons: null,
        PciLinkGeneration: null, PciLinkWidth: null, Processes: []);

    private sealed record WindowsGpuDetails(
        GpuEngineUsage[] Engines,
        GpuMemoryCounter[] Memory,
        GpuProcessMemoryUsage[] ProcessMemory,
        string[] Warnings);
}
