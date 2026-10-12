using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace PersonalWorkbench.Services;

/// <summary>Collects per-process TCP/UDP byte events while the ETW session is available.</summary>
public sealed class ProcessNetworkMonitor(ILogger<ProcessNetworkMonitor> logger) : IDisposable
{
    private readonly object _startLock = new();
    private readonly ConcurrentDictionary<int, TrafficCounter> _traffic = new();
    private TraceEventSession? _session;
    private bool _startAttempted;
    private string _status = "网络带宽仅支持 Windows ETW 采集；Linux/macOS 暂无通用的进程级网络计数器。";

    public (bool Available, string Note, IReadOnlyDictionary<int, (long Received, long Sent)> Counters) GetSnapshot()
    {
        EnsureStarted();
        var counters = _traffic.ToDictionary(pair => pair.Key,
            pair => (Interlocked.Read(ref pair.Value.Received), Interlocked.Read(ref pair.Value.Sent)));
        return (_session is not null, _status, counters);
    }

    private void EnsureStarted()
    {
        if (!OperatingSystem.IsWindows() || _session is not null || _startAttempted) return;
        lock (_startLock)
        {
            if (_session is not null || _startAttempted) return;
            _startAttempted = true;
            if (TraceEventSession.IsElevated() != true)
            {
                _status = "进程网络带宽不可用：Windows ETW 网络采集需要以管理员身份运行服务。";
                return;
            }

            TraceEventSession? session = null;
            try
            {
                session = new TraceEventSession($"PersonalWorkbench.Network.{Guid.NewGuid():N}");
                var kernel = session.Source.Kernel;
                kernel.TcpIpSend += data => Add(data.ProcessID, 0, data.size);
                kernel.TcpIpRecv += data => Add(data.ProcessID, data.size, 0);
                kernel.TcpIpSendIPV6 += data => Add(data.ProcessID, 0, data.size);
                kernel.TcpIpRecvIPV6 += data => Add(data.ProcessID, data.size, 0);
                kernel.UdpIpSend += data => Add(data.ProcessID, 0, data.size);
                kernel.UdpIpRecv += data => Add(data.ProcessID, data.size, 0);
                kernel.UdpIpSendIPV6 += data => Add(data.ProcessID, 0, data.size);
                kernel.UdpIpRecvIPV6 += data => Add(data.ProcessID, data.size, 0);
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
                _session = session;
                _status = "已按 ETW 网络事件统计进程 TCP/UDP 收发字节；速率按本次采样窗口计算。";
                _ = Task.Factory.StartNew(() => ProcessEvents(session), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                session?.Dispose();
                _status = "进程网络带宽不可用：ETW 网络会话启动失败，可能需要管理员权限或与现有内核跟踪会话冲突。";
                logger.LogInformation(exception, "无法启动进程网络 ETW 采集会话");
            }
        }
    }

    private void ProcessEvents(TraceEventSession session)
    {
        try { session.Source.Process(); }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "进程网络 ETW 采集已停止");
        }
        finally
        {
            lock (_startLock)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    _status = "进程网络带宽采集已停止：ETW 会话不可用。";
                }
                session.Dispose();
            }
        }
    }

    private void Add(int processId, long received, long sent)
    {
        if (processId <= 0) return;
        var counter = _traffic.GetOrAdd(processId, static _ => new TrafficCounter());
        if (received > 0) Interlocked.Add(ref counter.Received, received);
        if (sent > 0) Interlocked.Add(ref counter.Sent, sent);
    }

    public void Dispose()
    {
        lock (_startLock)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    private sealed class TrafficCounter
    {
        public long Received;
        public long Sent;
    }
}
