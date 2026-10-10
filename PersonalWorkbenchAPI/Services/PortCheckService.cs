using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;

using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>
/// 查询本机 TCP/UDP 端口占用情况，并将系统 PID 映射为进程名。
/// </summary>
public sealed class PortCheckService
{
    private readonly ILogger<PortCheckService> _logger;

    public PortCheckService(ILogger<PortCheckService> logger)
    {
        _logger = logger;
    }

    public async Task<PortCheckResponse> CheckAsync(int port, CancellationToken cancellationToken)
    {
        ValidatePort(port);

        var occupancies = await TryReadNetstatAsync(port, cancellationToken);
        if (occupancies.Count == 0)
        {
            occupancies = ReadManagedListeners(port);
        }

        return new PortCheckResponse(port, occupancies.Count > 0, occupancies);
    }

    public async Task<PortTerminateResponse> TerminateAsync(int port, int processId, CancellationToken cancellationToken)
    {
        ValidatePort(port);
        if (processId <= 0)
        {
            throw new ArgumentException("无效的进程 ID。", nameof(processId));
        }

        // 重新核对端口与 PID 的对应关系，避免客户端提交任意 PID。
        var current = await CheckAsync(port, cancellationToken);
        var occupancy = current.Occupancies.FirstOrDefault(item => item.ProcessId == processId);
        if (occupancy is null)
        {
            throw new InvalidOperationException("该进程当前未占用指定端口，可能已经退出或端口状态已变化。");
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(cancellationToken);
            return new PortTerminateResponse(port, processId, processName, $"已终止进程 {processName}（PID {processId}）。");
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("该进程已经退出。");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"没有权限终止该进程：{exception.Message}");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("该进程已经退出或无法再访问。");
        }
    }

    private async Task<IReadOnlyList<PortOccupancy>> TryReadNetstatAsync(int port, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default
            }
        };
        process.StartInfo.ArgumentList.Add("-ano");

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            _ = await errorTask;

            if (process.ExitCode != 0)
            {
                return [];
            }

            return ParseNetstat(output, port);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "读取 netstat 端口信息失败");
            return [];
        }
    }

    private static IReadOnlyList<PortOccupancy> ParseNetstat(string output, int port)
    {
        var results = new List<PortOccupancy>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || (fields[0] is not "TCP" and not "UDP"))
            {
                continue;
            }

            var protocol = fields[0];
            var localAddress = fields[1];
            if (!TryGetPort(localAddress, out var localPort) || localPort != port)
            {
                continue;
            }

            var processId = 0;
            _ = int.TryParse(fields[^1], out processId);
            var state = protocol == "TCP"
                ? fields.Length >= 5 ? fields[3] : "UNKNOWN"
                : "LISTENING";
            results.Add(new PortOccupancy(protocol, localAddress, state, processId, ResolveProcessName(processId)));
        }

        return results
            .GroupBy(item => $"{item.Protocol}|{item.LocalAddress}|{item.State}|{item.ProcessId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Protocol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LocalAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PortOccupancy> ReadManagedListeners(int port)
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var results = properties.GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == port)
            .Select(endpoint => new PortOccupancy("TCP", endpoint.ToString(), "LISTENING", 0, "未知进程"))
            .Concat(properties.GetActiveUdpListeners()
                .Where(endpoint => endpoint.Port == port)
                .Select(endpoint => new PortOccupancy("UDP", endpoint.ToString(), "LISTENING", 0, "未知进程")))
            .ToArray();
        return results;
    }

    private static bool TryGetPort(string address, out int port)
    {
        port = 0;
        var separator = address.LastIndexOf(':');
        return separator >= 0 && int.TryParse(address[(separator + 1)..].TrimEnd(']'), out port);
    }

    private static string ResolveProcessName(int processId)
    {
        if (processId <= 0)
        {
            return "未知进程";
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return $"PID {processId}";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return $"PID {processId}";
        }
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentException("端口号必须在 1 到 65535 之间。", nameof(port));
        }
    }
}
