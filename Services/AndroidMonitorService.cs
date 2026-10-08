using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace PersonalWorkbench.Services;

/// <summary>
/// 负责从 devices.txt 读取设备，通过 adb 持续抓取最新屏幕帧，并向 WebSocket 客户端推送。
/// </summary>
public sealed class AndroidMonitorService : IHostedService, IDisposable
{
    private const string DefaultDevicesFile = "devices.txt";
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AndroidMonitorService> _logger;
    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _shutdown;

    public AndroidMonitorService(IWebHostEnvironment environment, ILogger<AndroidMonitorService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        EnsureDeviceLoops();
        _ = ConnectAllAsync(_shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? shutdown;
        lock (_lifecycleLock)
        {
            shutdown = _shutdown;
            _shutdown = null;
        }

        if (shutdown is null)
        {
            return;
        }

        shutdown.Cancel();
        var tasks = _devices.Values.Select(state => state.CaptureTask).Where(task => task is not null).Cast<Task>();
        await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        shutdown.Dispose();
    }

    public IReadOnlyList<string> GetDevices()
    {
        var devices = ReadDeviceList();
        EnsureDeviceLoops(devices);
        return devices;
    }

    public async Task<string> ReconnectAsync(string device, CancellationToken cancellationToken)
    {
        ValidateDevice(device);
        var disconnect = await RunProcessAsync("adb", ["disconnect", device], cancellationToken);
        var connect = await RunProcessAsync("adb", ["connect", device], cancellationToken);
        EnsureDeviceLoops([device]);

        var output = Encoding.UTF8.GetString(connect.Output).Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Encoding.UTF8.GetString(disconnect.Output).Trim();
        }

        return output.Length == 0 ? "已发起重连" : output;
    }

    public async Task HandleWebSocketAsync(WebSocket socket, string device, CancellationToken cancellationToken)
    {
        ValidateDevice(device);
        EnsureDeviceLoops([device]);
        var state = _devices.GetOrAdd(device, _ => new DeviceState());
        var lastVersion = 0L;

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var frame = await state.WaitForFrameAsync(lastVersion, cancellationToken);
                if (frame is null)
                {
                    continue;
                }

                lastVersion = frame.Value.Version;
                await socket.SendAsync(frame.Value.Bytes, WebSocketMessageType.Binary, true, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "连接已关闭", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    private void EnsureDeviceLoops(IEnumerable<string>? devices = null)
    {
        if (_shutdown is null)
        {
            return;
        }

        foreach (var device in devices ?? ReadDeviceList())
        {
            if (!IsValidDevice(device))
            {
                continue;
            }

            var state = _devices.GetOrAdd(device, _ => new DeviceState());
            lock (state.SyncRoot)
            {
                if (state.CaptureTask is null or { IsCompleted: true })
                {
                    state.CaptureTask = CaptureLoopAsync(device, state, _shutdown.Token);
                }
            }
        }
    }

    private async Task CaptureLoopAsync(string device, DeviceState state, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await RunProcessAsync("adb", ["-s", device, "exec-out", "screencap", "-p"], cancellationToken);
                if (result.ExitCode == 0 && result.Output.Length > 100)
                {
                    var compressed = await ConvertToJpegAsync(result.Output, cancellationToken);
                    state.SetFrame(compressed is { Length: > 0 } ? compressed : result.Output);
                    await Task.Delay(150, cancellationToken);
                }
                else
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "抓取 Android 设备 {Device} 屏幕失败", device);
                await Task.Delay(800, cancellationToken);
            }
        }
    }

    private async Task ConnectAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var devices = ReadDeviceList();
            await Task.WhenAll(devices.Select(device => RunProcessAsync("adb", ["connect", device], cancellationToken)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "批量连接 Android 设备失败");
        }
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(-1, [], Encoding.UTF8.GetBytes(exception.Message));
        }

        var outputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, cancellationToken);
        var errorTask = ReadAllBytesAsync(process.StandardError.BaseStream, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // 进程已经退出时无需再次处理。
            }

            throw;
        }
    }

    private static async Task<byte[]?> ConvertToJpegAsync(byte[] png, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in new[]
                 {
                     "-f", "image2pipe", "-vcodec", "png", "-i", "pipe:0",
                     "-vf", "scale=400:-1", "-f", "image2", "-vcodec", "mjpeg", "-q:v", "7", "pipe:1"
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return null;
        }

        var outputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, cancellationToken);
        var errorTask = ReadAllBytesAsync(process.StandardError.BaseStream, cancellationToken);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(png, cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            _ = await errorTask;
            return process.ExitCode == 0 ? await outputTask : null;
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // 进程已经退出时无需再次处理。
            }

            throw;
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private IReadOnlyList<string> ReadDeviceList()
    {
        var path = Path.Combine(_environment.ContentRootPath, DefaultDevicesFile);
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && IsValidDevice(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateDevice(string device)
    {
        if (!IsValidDevice(device))
        {
            throw new ArgumentException("设备地址格式无效。", nameof(device));
        }
    }

    private static bool IsValidDevice(string device)
    {
        var value = device.Trim();
        var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return IPAddress.TryParse(parts[0], out _);
        }

        return parts.Length == 2 && IPAddress.TryParse(parts[0], out _) && int.TryParse(parts[1], out var port) && port is > 0 and <= 65535;
    }

    public void Dispose()
    {
        try
        {
            _shutdown?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // StopAsync 已经完成释放时无需重复取消。
        }
        finally
        {
            _shutdown?.Dispose();
            _shutdown = null;
        }
    }

    private readonly record struct ProcessResult(int ExitCode, byte[] Output, byte[] Error);

    private sealed class DeviceState
    {
        private readonly object _frameLock = new();
        private TaskCompletionSource<bool> _frameSignal = NewSignal();
        private byte[]? _frame;
        private long _version;

        public object SyncRoot { get; } = new();
        public Task? CaptureTask { get; set; }

        public void SetFrame(byte[] frame)
        {
            TaskCompletionSource<bool> signal;
            lock (_frameLock)
            {
                _frame = frame;
                _version++;
                signal = _frameSignal;
                _frameSignal = NewSignal();
            }

            signal.TrySetResult(true);
        }

        public async Task<(byte[] Bytes, long Version)?> WaitForFrameAsync(long lastVersion, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task signal;
                lock (_frameLock)
                {
                    if (_frame is not null && _version > lastVersion)
                    {
                        return (_frame, _version);
                    }

                    signal = _frameSignal.Task;
                }

                await signal.WaitAsync(cancellationToken);
            }
        }

        private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
