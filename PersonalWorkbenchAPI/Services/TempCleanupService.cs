using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>
/// Windows 临时文件清理入口。关机触发交给当前用户的独立计划任务，后端无需常驻；
/// HTTP 请求无法指定清理路径，清理器只接受其运行账户的 TEMP。
/// </summary>
public sealed class TempCleanupService(IWebHostEnvironment environment, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim operations = new(1, 1);

    private static string? UnsupportedReason()
    {
        if (!OperatingSystem.IsWindows()) return "该功能仅支持 Windows，操作对象为后端运行账户的 TEMP 目录。";
        using var identity = WindowsIdentity.GetCurrent();
        return identity.IsSystem || !Environment.UserInteractive
            ? "请使用交互式 Windows 用户运行后端，系统服务账户不支持此功能。" : null;
    }

    public async Task<TempCleanupStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (UnsupportedReason() is { } reason) return new() { Reason = reason };
        return await ExecuteAsync<TempCleanupStatus>("Status", cancellationToken);
    }

    public Task<TempCleanupResult> PreviewAsync(CancellationToken cancellationToken) => ExecuteAsync<TempCleanupResult>("Preview", cancellationToken);
    public Task<TempCleanupResult> CleanAsync(string expectedTempPath, CancellationToken cancellationToken) => ExecuteAsync<TempCleanupResult>("Clean", cancellationToken, expectedTempPath);
    public Task<TempCleanupStatus> SetAutomationAsync(bool enabled, string expectedTempPath, CancellationToken cancellationToken)
        => ExecuteAsync<TempCleanupStatus>(enabled ? "Enable" : "Disable", cancellationToken, expectedTempPath);

    private async Task<T> ExecuteAsync<T>(string action, CancellationToken cancellationToken, string? expectedTempPath = null)
    {
        if (UnsupportedReason() is { } reason) throw new InvalidOperationException(reason);
        if (action is "Clean" or "Enable" or "Disable" && string.IsNullOrWhiteSpace(expectedTempPath))
            throw new InvalidOperationException("缺少确认的 TEMP 路径，请先刷新状态。");
        if (!await operations.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("已有临时文件操作正在进行，请稍后重试。");
        try
        {
            var script = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Scripts", "TempCleanup.ps1"));
            if (!File.Exists(script)) throw new InvalidOperationException("临时清理脚本缺失，请重新构建或发布后端。");
            var stateDirectory = configuration["TempCleanup:StateDirectory"]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalWorkbench", "TempCleanup");
            if (!Path.IsPathFullyQualified(stateDirectory)) throw new InvalidOperationException("临时清理状态目录必须使用完整路径。");
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", action, "-StateDirectory", Path.GetFullPath(stateDirectory) })
                start.ArgumentList.Add(argument);
            if (expectedTempPath != null) { start.ArgumentList.Add("-ExpectedTempPath"); start.ArgumentList.Add(expectedTempPath); }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Windows 临时清理器。");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                // 只终止本次创建的子进程，清理器的进行中日志会保留，便于确认任务并未完整结束。
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (cancellationToken.IsCancellationRequested) throw;
                throw new InvalidOperationException("操作超时，清理器已停止；可以刷新查看上次记录。");
            }
            var json = await output;
            var errorText = await errors;
            if (process.ExitCode != 0)
            {
                string? message = null;
                try { using var document = JsonDocument.Parse(json); if (document.RootElement.TryGetProperty("message", out var failure)) message = failure.GetString(); } catch (JsonException) { }
                throw new InvalidOperationException(message ?? (string.IsNullOrWhiteSpace(errorText) ? "清理器执行失败。" : errorText.Trim()));
            }
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("清理器未返回有效结果。");
        }
        finally { operations.Release(); }
    }
}
