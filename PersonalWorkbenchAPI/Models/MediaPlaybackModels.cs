namespace PersonalWorkbench.Models;

/// <summary>网络视频播放请求；只接受明确支持的网络协议，不接受服务器文件路径。</summary>
public sealed class MediaPlaybackRequest
{
    public string Url { get; set; } = string.Empty;
    public bool IsLive { get; set; }
}

/// <summary>浏览器启动转流前查询的服务器能力与单文件上传限制。</summary>
public sealed record MediaPlaybackCapabilities(bool Available, string Message, long MaxUploadBytes);

/// <summary>转流会话状态；随机标识同时作为访问该会话播放列表与分片的凭据。</summary>
public sealed record MediaPlaybackSession(string Id, string Status, string PlaylistUrl, string? Error, bool IsLive);
