namespace PersonalWorkbench.Models;

/// <summary>
/// Android 设备监控列表响应。
/// </summary>
public sealed record AndroidDevicesResponse(IReadOnlyList<string> Devices);

/// <summary>
/// Android 设备重连响应。
/// </summary>
public sealed record AndroidReconnectResponse(string Status, string Message);
