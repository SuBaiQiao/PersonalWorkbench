using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using PersonalWorkbench.Models;

namespace PersonalWorkbench.Services;

/// <summary>
/// 将 RTSP、RTMP 或浏览器不兼容的视频转成 H.264 / AAC HLS。
/// 每个会话独立运行 FFmpeg，临时文件不进入静态目录，停止、闲置或应用退出后清理。
/// </summary>
public sealed class MediaPlaybackService : BackgroundService
{
    public const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _ffmpeg;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "personal-workbench-media", Guid.NewGuid().ToString("N"));
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly SemaphoreSlim _slots = new(3, 3);
    private readonly Lazy<Task<MediaPlaybackCapabilities>> _capabilities;

    private sealed class Session(string id, string directory)
    {
        public string Id { get; } = id;
        public string Directory { get; } = directory;
        public object Sync { get; } = new();
        public Process? Process;
        public DateTime LastAccess = DateTime.UtcNow;
        public DateTime Created = DateTime.UtcNow;
        public string ErrorTail = string.Empty;
        public string? Error;
        public bool Stopped;
        public bool IsLive;
    }

    public MediaPlaybackService(IConfiguration configuration)
    {
        _ffmpeg = configuration["MediaPlayback:FfmpegPath"] ?? "ffmpeg";
        _capabilities = new(CheckCapabilitiesAsync);
    }

    /// <summary>探测一次实际可执行能力，未安装 FFmpeg 时仍允许前端直接播放本地或网络视频。</summary>
    public Task<MediaPlaybackCapabilities> GetCapabilitiesAsync() => _capabilities.Value;

    private Process NewProcess() => new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = _ffmpeg, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true,
            RedirectStandardOutput = true
        }
    };

    private async Task<MediaPlaybackCapabilities> CheckCapabilitiesAsync()
    {
        try
        {
            using var process = NewProcess();
            process.StartInfo.ArgumentList.Add("-version");
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); return new(false, "FFmpeg 启动超时，请检查服务器配置。", MaxUploadBytes); }
            await Task.WhenAll(output, error);
            return new(process.ExitCode == 0, process.ExitCode == 0 ? "服务端兼容播放可用" : "FFmpeg 无法正常启动。", MaxUploadBytes);
        }
        catch { return new(false, "服务器未找到 FFmpeg；安装后加入 PATH，或设置 MediaPlayback:FfmpegPath 并重启服务。", MaxUploadBytes); }
    }

    /// <summary>创建网络会话；RTSP 强制使用 TCP，直播仅保留最近的 HLS 分片。</summary>
    public async Task<MediaPlaybackSession> CreateAsync(MediaPlaybackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url) || request.Url.Any(char.IsControl) || request.Url.Length > 8192 || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "rtsp" or "rtmp" or "rtmps") || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("请输入有效的 HTTP、HTTPS、RTSP、RTMP 或 RTMPS 视频地址。");
        await RequireAvailableAsync();
        var session = Reserve();
        try
        {
            Start(session, request.Url, request.IsLive || uri.Scheme is "rtsp" or "rtmp" or "rtmps", uri.Scheme);
            return Describe(session);
        }
        catch { await RemoveAsync(session.Id); throw; }
    }

    /// <summary>上传兼容播放使用独立文件，不允许把本地播放列表伪装为视频来读取其他服务器文件。</summary>
    public async Task<MediaPlaybackSession> CreateUploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxUploadBytes) throw new ArgumentException("请选择非空视频文件，兼容播放单文件最大 2GB。");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!new[] { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".flv", ".ts", ".mts", ".m2ts", ".mpg", ".mpeg", ".wmv", ".ogv", ".ogg", ".rm", ".rmvb", ".3gp" }.Contains(extension))
            throw new ArgumentException("请选择常见视频文件；本地 m3u8 请通过浏览器的文件夹入口打开。");
        await RequireAvailableAsync();
        var session = Reserve();
        try
        {
            var input = Path.Combine(session.Directory, "source" + extension);
            await using (var output = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(output, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Start(session, input, false, null);
            return Describe(session);
        }
        catch { await RemoveAsync(session.Id); throw; }
    }

    private async Task RequireAvailableAsync()
    {
        var capability = await GetCapabilitiesAsync();
        if (!capability.Available) throw new InvalidOperationException(capability.Message);
    }

    private Session Reserve()
    {
        if (!_slots.Wait(0)) throw new InvalidOperationException("服务器最多同时运行 3 个兼容播放会话，请先停止其他播放。");
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var session = new Session(id, Path.Combine(_root, id));
            Directory.CreateDirectory(session.Directory);
            _sessions[id] = session;
            return session;
        }
        catch { _slots.Release(); throw; }
    }

    private void Start(Session session, string input, bool live, string? protocol)
    {
        session.IsLive = live;
        var process = NewProcess();
        session.Process = process;
        // 使用 ArgumentList 逐项传参，URL、密码和文件名都不会被 shell 当作命令执行。
        var arguments = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" };
        if (protocol == "rtsp") arguments.AddRange(["-rtsp_transport", "tcp", "-timeout", "5000000"]);
        else if (protocol != null) arguments.AddRange(["-rw_timeout", live ? "5000000" : "15000000"]);
        // 直播缩短探测与解复用缓冲，避免摄像头已产生新画面但转码进程仍积压旧帧。
        // 保留探测期间的包；nobuffer 会丢弃首段音频，导致后续分片突然新增音轨而使浏览器缓冲失败。
        if (live) arguments.AddRange(["-fflags", "+discardcorrupt", "-flags", "low_delay",
            "-analyzeduration", "1000000", "-probesize", "262144", "-max_delay", "500000"]);
        arguments.AddRange(["-protocol_whitelist", protocol == null ? "file" : "http,https,tcp,tls,crypto,rtsp,rtp,udp,rtmp,rtmps"]);
        if (protocol == null) arguments.AddRange(["-format_whitelist", "mov,matroska,webm,avi,flv,mpeg,mpegts,ogg,asf,rm"]);
        arguments.AddRange(["-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-sn", "-dn",
            "-c:v", "libx264", "-preset", live ? "ultrafast" : "veryfast", "-tune", "zerolatency", "-pix_fmt", "yuv420p",
            "-vf", live ? "scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,fps=25" : "scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-crf", "23", "-threads", "2",
            "-force_key_frames", live ? "expr:gte(t,n_forced)" : "expr:gte(t,n_forced*2)", "-c:a", "aac", "-b:a", "128k", "-ac", "2"]);
        // 固定一秒 GOP 并关闭 B 帧，保证 HLS 每秒都有可独立解码的新分片，而不是等待场景关键帧。
        if (live) arguments.AddRange(["-g", "25", "-keyint_min", "25", "-sc_threshold", "0", "-bf", "0",
            "-af", "aresample=async=1:first_pts=0", "-hls_delete_threshold", "4"]);
        arguments.AddRange(["-f", "hls", "-hls_time", live ? "1" : "2", "-hls_list_size", live ? "12" : "0",
            "-hls_flags", live ? "delete_segments+independent_segments+temp_file" : "independent_segments+temp_file",
            "-hls_segment_filename", Path.Combine(session.Directory, "segment_%06d.ts"), Path.Combine(session.Directory, "index.m3u8")]);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null) return;
            lock (session.Sync)
            {
                // 只保存用于判断错误类别的短尾部，不向浏览器回传可能含账号密码的原始日志。
                session.ErrorTail = (session.ErrorTail + "\n" + eventArgs.Data);
                if (session.ErrorTail.Length > 4096) session.ErrorTail = session.ErrorTail[^4096..];
            }
        };
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
    }

    /// <summary>状态轮询也是会话心跳，暂停播放时仍可保持会话，关闭页面后则自动过期。</summary>
    public MediaPlaybackSession Get(string id)
    {
        if (!_sessions.TryGetValue(id, out var session)) throw new KeyNotFoundException("播放会话已结束或过期。");
        lock (session.Sync) { session.LastAccess = DateTime.UtcNow; return Describe(session); }
    }

    private static MediaPlaybackSession Describe(Session session)
    {
        lock (session.Sync)
        {
            if (session.Stopped) throw new KeyNotFoundException("播放会话已结束。");
            var exited = session.Process?.HasExited == true;
            if (exited && session.Process!.ExitCode != 0 && session.Error == null)
                session.Error = session.ErrorTail.Contains("401", StringComparison.OrdinalIgnoreCase) || session.ErrorTail.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                    ? "视频源认证失败，请检查地址中的账号、密码或访问令牌。" : "无法读取或转换视频源，请检查地址、服务器网络和视频编码。";
            // 摄像头断开时 FFmpeg 可能以 0 正常退出；直播没有“文件播完”，仍需通知前端重连。
            if (exited && session.IsLive && session.Error == null) session.Error = "直播连接已结束，需要重新连接视频源。";
            var ready = File.Exists(Path.Combine(session.Directory, "index.m3u8"));
            if (exited && !ready && session.Error == null) session.Error = "视频源未产生可播放的视频分片。";
            var status = session.Error != null ? "failed" : ready ? exited ? "completed" : "ready" : "starting";
            return new(session.Id, status, $"/api/media/sessions/{session.Id}/index.m3u8", session.Error, session.IsLive);
        }
    }

    /// <summary>仅允许读取本会话生成的清单与分片，禁止目录穿越和读取上传原文件。</summary>
    public FileStream OpenFile(string id, string fileName)
    {
        if (!Regex.IsMatch(fileName, @"^(index\.m3u8|segment_[0-9]{6,}\.ts)$", RegexOptions.CultureInvariant))
            throw new KeyNotFoundException("播放文件不存在。");
        if (!_sessions.TryGetValue(id, out var session)) throw new KeyNotFoundException("播放会话已结束或过期。");
        lock (session.Sync)
        {
            if (session.Stopped) throw new KeyNotFoundException("播放会话已结束。");
            session.LastAccess = DateTime.UtcNow;
            return new FileStream(Path.Combine(session.Directory, fileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
    }

    /// <summary>停止进程并删除本会话的临时文件；重复停止同一会话也安全。</summary>
    public async Task RemoveAsync(string id)
    {
        if (!_sessions.TryRemove(id, out var session)) return;
        try
        {
            lock (session.Sync)
            {
                session.Stopped = true;
                if (session.Process is { } process)
                {
                    try { if (!process.HasExited) process.Kill(true); }
                    catch (InvalidOperationException) { /* 进程可能尚未启动或刚刚退出。 */ }
                }
            }
            if (session.Process is { } running)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await running.WaitForExitAsync(timeout.Token); }
                catch (InvalidOperationException) { }
                catch (OperationCanceledException) { }
                running.Dispose();
            }
            // 删除前验证完整路径，只允许清理本服务创建的随机会话子目录。
            var fullPath = Path.GetFullPath(session.Directory);
            if (fullPath.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try { Directory.Delete(fullPath, true); break; }
                    catch (DirectoryNotFoundException) { break; }
                    catch (IOException) { await Task.Delay(150); }
                    catch (UnauthorizedAccessException) { await Task.Delay(150); }
                }
            }
        }
        finally { _slots.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var session in _sessions.Values)
                {
                    bool expired;
                    lock (session.Sync)
                    {
                        expired = DateTime.UtcNow - session.LastAccess > TimeSpan.FromMinutes(2)
                            || DateTime.UtcNow - session.Created > TimeSpan.FromHours(4);
                        if (!expired && !session.Stopped && session.Process is { HasExited: false } process)
                        {
                            var stalled = DateTime.UtcNow - session.Created > TimeSpan.FromSeconds(60)
                                && !File.Exists(Path.Combine(session.Directory, "index.m3u8"));
                            // 进程存活不代表视频仍在更新；直播清单停止推进时让前端收到失败并重新连接源。
                            var playlistPath = Path.Combine(session.Directory, "index.m3u8");
                            var frozenLive = session.IsLive && File.Exists(playlistPath)
                                && DateTime.UtcNow - File.GetLastWriteTimeUtc(playlistPath) > TimeSpan.FromSeconds(15);
                            var tooLarge = OutputBytes(session.Directory) > MaxUploadBytes;
                            if (stalled || frozenLive || tooLarge)
                            {
                                session.Error = stalled ? "视频源在 60 秒内未返回可播放内容，请检查网络与地址。"
                                    : frozenLive ? "直播视频源停止更新，正在等待重新连接。" : "兼容播放缓存已达到 2GB 上限，请重新打开视频。";
                                try { process.Kill(true); }
                                catch (InvalidOperationException) { /* 检查期间进程可能正常退出。 */ }
                            }
                        }
                    }
                    if (expired) await RemoveAsync(session.Id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await Task.WhenAll(_sessions.Keys.Select(RemoveAsync));
            try { if (Directory.Exists(_root)) Directory.Delete(_root); }
            catch (IOException) { /* 仍有分片响应在读取时交给操作系统临时目录后续清理。 */ }
        }
    }

    /// <summary>直播分片可能被 FFmpeg 同时轮换删除，统计时忽略刚刚消失的文件。</summary>
    private static long OutputBytes(string directory)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "segment_*.ts"))
            {
                try { total += new FileInfo(file).Length; }
                catch (FileNotFoundException) { }
            }
        }
        catch (DirectoryNotFoundException) { }
        return total;
    }
}
