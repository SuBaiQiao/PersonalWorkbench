using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

public sealed partial class SystemStatusService
{
    private readonly SemaphoreSlim _processesLock = new(1, 1);
    private ProcessListResponse? _processes;

    public async Task<ProcessListResponse> GetProcessesAsync(CancellationToken cancellationToken)
    {
        await _processesLock.WaitAsync(cancellationToken);
        try
        {
            if (_processes is not null && DateTimeOffset.UtcNow - _processes.CollectedAt < TimeSpan.FromSeconds(2))
                return _processes;

            var networkBefore = networkMonitor.GetSnapshot();
            var first = ReadProcesses();
            await Task.Delay(750, cancellationToken);
            var second = ReadProcesses();
            var networkAfter = networkMonitor.GetSnapshot();
            var interval = Math.Max(1, (int)(second.CapturedAt - first.CapturedAt).TotalMilliseconds);
            var seconds = interval / 1000d;
            var previous = first.Processes.ToDictionary(item => (item.ProcessId, item.StartIdentity));
            var networkAvailable = networkBefore.Available && networkAfter.Available;
            var networkCounters = networkAfter.Counters;
            var connectionCounts = OperatingSystem.IsWindows() ? ReadWindowsConnectionCounts() : null;
            var gpuDetails = await GetGpuDetailsAsync(cancellationToken);
            var gpuUsage = gpuDetails.Engines.Where(item => item.ProcessId is > 0 && item.UsagePercent is not null)
                .GroupBy(item => item.ProcessId!.Value)
                .ToDictionary(group => group.Key, group => (double?)group.Max(item => item.UsagePercent!.Value));
            var nvidiaMemory = gpuDetails.Adapters.SelectMany(adapter => adapter.Processes)
                .Where(item => item.ProcessId > 0 && item.UsedMemoryBytes is not null)
                .GroupBy(item => item.ProcessId).ToDictionary(group => group.Key, group => group.Sum(item => item.UsedMemoryBytes!.Value));
            var windowsGpuMemory = gpuDetails.ProcessMemory.Where(item => item.ProcessId > 0)
                .GroupBy(item => item.ProcessId)
                .ToDictionary(group => group.Key, group => group.Sum(item => (item.DedicatedBytes ?? 0) + (item.SharedBytes ?? 0)));
            var useWindowsGpuMemory = windowsGpuMemory.Count > 0;

            var rows = second.Processes.Select(current =>
            {
                previous.TryGetValue((current.ProcessId, current.StartIdentity), out var before);
                var cpu = before?.CpuTime is { } oldCpu && current.CpuTime is { } newCpu && newCpu >= oldCpu
                    ? Math.Clamp(100d * (newCpu - oldCpu).TotalMilliseconds / (interval * Environment.ProcessorCount), 0, 100)
                    : (double?)null;
                long? rate(long? oldValue, long? newValue) => oldValue is not null && newValue is not null && newValue >= oldValue
                    ? (long)Math.Round((newValue.Value - oldValue.Value) / seconds)
                    : null;
                long? networkRate(int processId, bool received)
                {
                    if (!networkAvailable) return null;
                    var oldCount = networkBefore.Counters.TryGetValue(processId, out var oldCounter) ? (received ? oldCounter.Received : oldCounter.Sent) : 0;
                    var newCount = networkCounters.TryGetValue(processId, out var newCounter) ? (received ? newCounter.Received : newCounter.Sent) : oldCount;
                    return newCount >= oldCount ? (long)Math.Round((newCount - oldCount) / seconds) : null;
                }

                long? processGpuMemory = useWindowsGpuMemory
                    ? windowsGpuMemory.TryGetValue(current.ProcessId, out var windowsBytes) ? windowsBytes : null
                    : nvidiaMemory.TryGetValue(current.ProcessId, out var nvidiaBytes) ? nvidiaBytes : null;
                var connections = (Tcp: 0, Udp: 0);
                if (connectionCounts is not null) connectionCounts.TryGetValue(current.ProcessId, out connections);
                return new ProcessUsage(current.ProcessId, current.Name, current.FilePath, current.StartedAt,
                    current.ParentProcessId, current.SessionId, cpu is null ? null : Math.Round(cpu.Value, 1),
                    current.WorkingSetBytes, current.PrivateMemoryBytes, current.VirtualMemoryBytes, current.PagedMemoryBytes,
                    rate(before?.ReadBytes, current.ReadBytes), rate(before?.WriteBytes, current.WriteBytes),
                    networkRate(current.ProcessId, true), networkRate(current.ProcessId, false), current.ThreadCount,
                    current.HandleCount, current.BasePriority, gpuUsage.GetValueOrDefault(current.ProcessId),
                    processGpuMemory, "运行中", current.Error,
                    connectionCounts is null ? null : connections.Tcp,
                    connectionCounts is null ? null : connections.Udp);
            }).OrderByDescending(item => item.CpuPercent ?? -1)
                .ThenByDescending(item => item.WorkingSetBytes ?? -1).ToArray();

            var warnings = new List<string>();
            if (!networkAvailable) warnings.Add(networkAfter.Note);
            var inaccessible = rows.Count(item => item.Error is not null);
            if (inaccessible > 0) warnings.Add($"有 {inaccessible} 个进程的部分数据因权限不足或进程退出而不可用。");
            if (OperatingSystem.IsLinux()) warnings.Add("Linux 读取进程级磁盘 I/O 计数；GPU 进程指标取决于 NVIDIA 驱动，进程级网络带宽暂不支持。");

            _processes = new ProcessListResponse(DateTimeOffset.UtcNow, interval, Environment.ProcessorCount,
                networkAvailable, networkAfter.Note,
                connectionCounts?.Values.Sum(item => item.Tcp), connectionCounts?.Values.Sum(item => item.Udp), rows, warnings);
            return _processes;
        }
        finally { _processesLock.Release(); }
    }

    private static ProcessSnapshot ReadProcesses()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var processes = new List<RawProcess>();
        var windowsParents = OperatingSystem.IsWindows() ? ReadWindowsParentProcessIds() : null;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var id = Safe(() => process.Id, -1);
                if (id < 0) continue;
                var name = Safe(() => process.ProcessName, $"PID {id}");
                var started = Safe<DateTime?>(() => process.StartTime, null);
                DateTimeOffset? startedAt = started is null ? null : new DateTimeOffset(started.Value.ToUniversalTime());
                var cpu = Safe<TimeSpan?>(() => process.TotalProcessorTime, null);
                var io = ReadIo(process);
                var workingSet = Safe<long?>(() => process.WorkingSet64, null);
                var error = cpu is null && workingSet is null
                    ? "访问受限或进程已退出"
                    : null;

                processes.Add(new RawProcess(id, startedAt?.Ticks, name,
                    Safe<string?>(() => process.MainModule?.FileName, null), startedAt,
                    OperatingSystem.IsLinux() ? ReadLinuxParentProcessId(id)
                        : windowsParents is not null && windowsParents.TryGetValue(id, out var parentId) ? parentId : null,
                    Safe<int?>(() => process.SessionId, null), cpu,
                    workingSet, Safe<long?>(() => process.PrivateMemorySize64, null),
                    Safe<long?>(() => process.VirtualMemorySize64, null), Safe<long?>(() => process.PagedMemorySize64, null),
                    io.Read, io.Write, Safe<int?>(() => process.Threads.Count, null),
                    Safe<int?>(() => process.HandleCount, null), Safe<int?>(() => process.BasePriority, null), error));
            }
        }
        return new ProcessSnapshot(capturedAt, processes);
    }

    private static (long? Read, long? Write) ReadIo(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return GetProcessIoCounters(process.SafeHandle, out var counters)
                    ? ((long)counters.ReadTransferCount, (long)counters.WriteTransferCount)
                    : (null, null);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException)
            { return (null, null); }
        }
        if (!OperatingSystem.IsLinux()) return (null, null);
        try
        {
            var values = File.ReadLines($"/proc/{process.Id}/io").Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => long.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
            return (values.GetValueOrDefault("read_bytes"), values.GetValueOrDefault("write_bytes"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.ComponentModel.Win32Exception)
        { return (null, null); }
    }

    private static int? ReadLinuxParentProcessId(int processId)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{processId}/stat");
            var endName = stat.LastIndexOf(')');
            var fields = stat[(endName + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > 1 && int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent) ? parent : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    private static Dictionary<int, int> ReadWindowsParentProcessIds()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == new IntPtr(-1)) return parents;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) return parents;
            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
            } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return parents;
    }

    private static Dictionary<int, (int Tcp, int Udp)> ReadWindowsConnectionCounts()
    {
        var counts = new Dictionary<int, (int Tcp, int Udp)>();
        ReadConnectionTable(2, 5, 24, 20, counts, true);
        ReadConnectionTable(2, 1, 12, 8, counts, false);
        ReadConnectionTable(23, 5, 56, 52, counts, true);
        ReadConnectionTable(23, 1, 28, 24, counts, false);
        return counts;
    }

    private static void ReadConnectionTable(int family, int tableClass, int rowSize, int processIdOffset,
        Dictionary<int, (int Tcp, int Udp)> counts, bool tcp)
    {
        try
        {
            QueryConnectionTable(IntPtr.Zero, out var size, family, tableClass, tcp);
            if (size == 0) return;
            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (QueryConnectionTable(buffer, out size, family, tableClass, tcp) != 0) return;
                var number = Marshal.ReadInt32(buffer);
                for (var index = 0; index < number; index++)
                {
                    var processId = Marshal.ReadInt32(buffer, sizeof(uint) + index * rowSize + processIdOffset);
                    if (processId <= 0) continue;
                    counts.TryGetValue(processId, out var current);
                    counts[processId] = tcp ? (current.Tcp + 1, current.Udp) : (current.Tcp, current.Udp + 1);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or OverflowException)
        { }
    }

    private static uint QueryConnectionTable(IntPtr buffer, out uint size, int family, int tableClass, bool tcp)
        => tcp ? GetExtendedTcpTable(buffer, out size, false, family, tableClass, 0)
            : GetExtendedUdpTable(buffer, out size, false, family, tableClass, 0);

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
            or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException or IOException or OverflowException)
        { return fallback; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(Microsoft.Win32.SafeHandles.SafeProcessHandle process, out ProcessIoCounters counters);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, out uint size, [MarshalAs(UnmanagedType.Bool)] bool ordered,
        int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, out uint size, [MarshalAs(UnmanagedType.Bool)] bool ordered,
        int addressFamily, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessIoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableFile;
    }

    private sealed record ProcessSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<RawProcess> Processes);

    private sealed record RawProcess(
        int ProcessId,
        long? StartIdentity,
        string Name,
        string? FilePath,
        DateTimeOffset? StartedAt,
        int? ParentProcessId,
        int? SessionId,
        TimeSpan? CpuTime,
        long? WorkingSetBytes,
        long? PrivateMemoryBytes,
        long? VirtualMemoryBytes,
        long? PagedMemoryBytes,
        long? ReadBytes,
        long? WriteBytes,
        int? ThreadCount,
        int? HandleCount,
        int? BasePriority,
        string? Error);
}
