using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>Reads the API host, with shared sampling and optional driver telemetry.</summary>
public sealed partial class SystemStatusService(ILogger<SystemStatusService> logger, ProcessNetworkMonitor networkMonitor)
{
    private readonly SemaphoreSlim _samplingLock = new(1, 1);
    private SystemStatusResponse? _snapshot;
    private CpuTimes? _previousCpu;
    private WindowsHardware? _hardware;
    private DateTimeOffset _hardwareUpdatedAt;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _samplingLock.WaitAsync(cancellationToken);
        try
        {
            // Multiple viewers share a sample instead of launching duplicate system commands.
            if (_snapshot is not null && DateTimeOffset.UtcNow - _snapshot.CollectedAt < TimeSpan.FromSeconds(3))
                return _snapshot;

            var warnings = new List<string>();
            if (OperatingSystem.IsWindows() && (_hardware is null || DateTimeOffset.UtcNow - _hardwareUpdatedAt > TimeSpan.FromMinutes(5)))
            {
                _hardware = await ReadWindowsHardwareAsync(cancellationToken);
                _hardwareUpdatedAt = DateTimeOffset.UtcNow;
            }

            var usage = await ReadCpuUsageAsync(warnings, cancellationToken);
            var cpu = new CpuStatus(_hardware?.CpuName, _hardware?.PhysicalCores,
                _hardware?.LogicalProcessors ?? Environment.ProcessorCount, usage);
            if (OperatingSystem.IsLinux()) cpu = ReadLinuxCpu(cpu, warnings);
            if (OperatingSystem.IsWindows() && _hardware is null)
                warnings.Add("CPU 型号与物理核心数暂时不可用，请确认系统 CIM 服务可用。");

            var memory = ReadMemory(warnings);
            var gpu = await ReadGpuAsync(cancellationToken);
            var disks = ReadDisks(warnings);
            _snapshot = new SystemStatusResponse(DateTimeOffset.UtcNow, Environment.MachineName,
                RuntimeInformation.OSDescription, Environment.TickCount64 / 1000,
                cpu, memory, gpu, disks, warnings);
            return _snapshot;
        }
        finally { _samplingLock.Release(); }
    }

    private async Task<double?> ReadCpuUsageAsync(List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var current = ReadCpuTimes();
            if (_previousCpu is null)
            {
                _previousCpu = current;
                await Task.Delay(500, cancellationToken);
                current = ReadCpuTimes();
            }

            var previous = _previousCpu.Value;
            _previousCpu = current;
            if (current.Total <= previous.Total || current.Idle < previous.Idle) return null;
            var total = current.Total - previous.Total;
            var idle = current.Idle - previous.Idle;
            return Math.Round(Math.Clamp(100d * (1 - (double)idle / total), 0, 100), 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "读取 CPU 使用率失败");
            warnings.Add("当前系统无法读取整机 CPU 使用率。");
            return null;
        }
    }

    private static CpuTimes ReadCpuTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                throw new InvalidOperationException("GetSystemTimes failed.");
            // Windows kernel time already includes idle time.
            return new CpuTimes(idle, kernel + user);
        }
        if (OperatingSystem.IsLinux())
        {
            var fields = File.ReadLines("/proc/stat").First().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // guest and guest_nice are already included in user/nice; exclude them from the sum.
            var times = fields.Skip(1).Take(8).Select(value => ulong.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            return new CpuTimes(times[3] + (times.Length > 4 ? times[4] : 0), times.Aggregate(0UL, (sum, value) => sum + value));
        }
        throw new PlatformNotSupportedException();
    }

    private static CpuStatus ReadLinuxCpu(CpuStatus cpu, List<string> warnings)
    {
        try
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            var name = lines.FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim();
            var processors = lines.Count(line => line.StartsWith("processor", StringComparison.Ordinal));
            var cores = File.ReadAllText("/proc/cpuinfo").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(block => block.Split('\n').Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim()))
                .Where(block => block.ContainsKey("physical id") && block.ContainsKey("core id"))
                .Select(block => $"{block["physical id"]}:{block["core id"]}").Distinct().Count();
            return cpu with { Name = name, LogicalProcessors = processors > 0 ? processors : cpu.LogicalProcessors, PhysicalCores = cores > 0 ? cores : null };
        }
        catch { warnings.Add("CPU 型号或核心数读取失败。"); return cpu; }
    }

    private static MemoryStatus ReadMemory(List<string> warnings)
    {
        try
        {
            long total;
            long available;
            if (OperatingSystem.IsWindows())
            {
                var state = new MemoryState { Length = (uint)Marshal.SizeOf<MemoryState>() };
                if (!GlobalMemoryStatusEx(ref state)) throw new InvalidOperationException("GlobalMemoryStatusEx failed.");
                total = checked((long)state.TotalPhysical);
                available = checked((long)state.AvailablePhysical);
            }
            else if (OperatingSystem.IsLinux())
            {
                var values = File.ReadLines("/proc/meminfo").Select(line => line.Split(':', 2))
                    .ToDictionary(parts => parts[0], parts => long.Parse(parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], CultureInfo.InvariantCulture) * 1024);
                total = values["MemTotal"];
                available = values.TryGetValue("MemAvailable", out var value)
                    ? value : values["MemFree"] + values.GetValueOrDefault("Buffers") + values.GetValueOrDefault("Cached");
            }
            else throw new PlatformNotSupportedException();
            available = Math.Clamp(available, 0, total);
            return new MemoryStatus(total, available, total - available, Percent(total - available, total));
        }
        catch { warnings.Add("当前系统无法读取物理内存状态。"); return new MemoryStatus(null, null, null, null); }
    }

    private static IReadOnlyList<DiskStatus> ReadDisks(List<string> warnings)
    {
        var disks = new List<DiskStatus>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Network shares and optical media can block for a long time when disconnected.
                if (drive.DriveType is not DriveType.Fixed and not DriveType.Removable) continue;
                try
                {
                    if (!drive.IsReady)
                    {
                        disks.Add(new DiskStatus(drive.Name, null, null, drive.DriveType.ToString(), false, null, null, null, null, "设备未就绪"));
                        continue;
                    }
                    var total = drive.TotalSize;
                    var free = Math.Clamp(drive.TotalFreeSpace, 0, total);
                    disks.Add(new DiskStatus(drive.Name, drive.VolumeLabel, drive.DriveFormat, drive.DriveType.ToString(), true,
                        total, total - free, free, Percent(total - free, total), null));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    disks.Add(new DiskStatus(drive.Name, null, null, drive.DriveType.ToString(), false, null, null, null, null, "无法读取此磁盘，可能未就绪或权限不足"));
                }
            }
        }
        catch { warnings.Add("磁盘列表读取失败。"); }
        return disks;
    }

    private async Task<WindowsHardware?> ReadWindowsHardwareAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $cpu = @(Get-CimInstance Win32_Processor -ErrorAction Stop)
            $gpu = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object {
                @{ Id = $_.PNPDeviceID; Name = $_.Name; DriverVersion = $_.DriverVersion }
            })
            @{
                CpuName = (($cpu | Select-Object -ExpandProperty Name -Unique) -join ' / ')
                PhysicalCores = [int]($cpu | Measure-Object NumberOfCores -Sum).Sum
                LogicalProcessors = [int]($cpu | Measure-Object NumberOfLogicalProcessors -Sum).Sum
                Adapters = $gpu
            } | ConvertTo-Json -Depth 4 -Compress
            """;
        var output = await RunPowerShellAsync(script, cancellationToken);
        try { return output is null ? null : JsonSerializer.Deserialize<WindowsHardware>(output, JsonOptions); }
        catch (JsonException exception) { logger.LogDebug(exception, "解析硬件信息失败"); return null; }
    }

    private async Task<GpuStatus> ReadGpuAsync(CancellationToken cancellationToken)
    {
        var adapters = new List<GpuAdapterStatus>();
        var output = await RunCommandAsync("nvidia-smi",
            ["--query-gpu=uuid,name,driver_version,utilization.gpu,memory.total,memory.used,temperature.gpu", "--format=csv,noheader,nounits"],
            TimeSpan.FromSeconds(3), cancellationToken);
        if (output is not null)
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(',').Select(field => field.Trim()).ToArray();
                if (fields.Length != 7) continue;
                adapters.Add(new GpuAdapterStatus(fields[0], fields[1], fields[2], Number(fields[3]),
                    MiB(fields[4]), MiB(fields[5]), Number(fields[6])));
            }
        }

        if (OperatingSystem.IsWindows())
        {
            // CIM AdapterRAM is a uint32 and truncates cards over 4 GiB, so it is not used for VRAM totals.
            // Keep all Windows adapters visible on mixed NVIDIA/integrated systems.
            var hardwareAdapters = _hardware?.Adapters ?? [];
            foreach (var adapter in hardwareAdapters)
            {
                if (adapters.Any(item => item.Name.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase))) continue;
                adapters.Add(new GpuAdapterStatus(adapter.Id, adapter.Name, adapter.DriverVersion, null, null, null, null));
            }
            const string script = """
                $engines = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop)
                $memory = @(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory -ErrorAction Stop)
                $groups = @($engines | Group-Object { $_.Name -replace '^pid_\d+_', '' } | ForEach-Object {
                    ($_.Group | Measure-Object UtilizationPercentage -Sum).Sum
                })
                @{
                    UsagePercent = if ($groups.Count -gt 0) { [Math]::Min(100, ($groups | Measure-Object -Maximum).Maximum) } else { $null }
                    DedicatedUsedBytes = if ($memory.Count -gt 0) { [long]($memory | Measure-Object DedicatedUsage -Sum).Sum } else { $null }
                    SharedUsedBytes = if ($memory.Count -gt 0) { [long]($memory | Measure-Object SharedUsage -Sum).Sum } else { $null }
                } | ConvertTo-Json -Compress
                """;
            // Adapter names/indices in GPU counters cannot reliably be mapped to CIM devices.
            // Expose system totals separately, and use NVIDIA telemetry only for individual cards.
            var counters = await RunPowerShellAsync(script, cancellationToken);
            try
            {
                var metrics = counters is null ? null : JsonSerializer.Deserialize<WindowsGpuMetrics>(counters, JsonOptions);
                if (metrics is not null)
                    return new GpuStatus(adapters, metrics.UsagePercent, metrics.DedicatedUsedBytes, metrics.SharedUsedBytes,
                        "整机 GPU 使用率取最繁忙引擎，显存占用为所有显卡合计；单卡实时指标取决于驱动支持。");
            }
            catch (JsonException exception) { logger.LogDebug(exception, "解析 GPU 计数器失败"); }
        }

        return new GpuStatus(adapters, null, null, null,
            adapters.Count == 0 ? "未检测到可读取的显卡。Windows 使用系统驱动信息，其他系统目前支持 nvidia-smi。"
                : "整机 GPU 计数器不可用；单卡使用率、显存与温度需要 NVIDIA 驱动提供 nvidia-smi，其他指标显示不可用。");
    }

    private Task<string?> RunPowerShellAsync(string script, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); " + script));
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        return RunCommandAsync(executable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], timeout ?? TimeSpan.FromSeconds(5), cancellationToken);
    }

    private async Task<string?> RunCommandAsync(string fileName, string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            _ = await stderr;
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is OperationCanceledException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogDebug(exception, "系统状态命令不可用或超时: {Command}", fileName);
            return null;
        }
        finally
        {
            // Cancellation/timeouts must not leave a driver query or PowerShell running.
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            { logger.LogDebug(exception, "清理系统状态命令失败"); }
        }
    }

    private static double? Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number) && number >= 0 ? number : null;
    private static long? MiB(string value) => Number(value) is { } number && number <= long.MaxValue / 1048576d ? (long)(number * 1048576) : null;
    private static double? Percent(long used, long total) => total > 0 ? Math.Round(Math.Clamp(100d * used / total, 0, 100), 1) : null;

    private readonly record struct CpuTimes(ulong Idle, ulong Total);
    private sealed record WindowsAdapter(string Id, string Name, string? DriverVersion);
    private sealed record WindowsHardware(string? CpuName, int PhysicalCores, int LogicalProcessors, WindowsAdapter[] Adapters);
    private sealed record WindowsGpuMetrics(double? UsagePercent, long? DedicatedUsedBytes, long? SharedUsedBytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryState
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryState state);
}
