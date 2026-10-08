namespace PersonalWorkbench.Models;

public sealed class PortCheckRequest
{
    public int Port { get; set; }
}

public sealed class PortTerminateRequest
{
    public int Port { get; set; }

    public int ProcessId { get; set; }
}

public sealed record PortOccupancy(
    string Protocol,
    string LocalAddress,
    string State,
    int ProcessId,
    string ProcessName);

public sealed record PortCheckResponse(
    int Port,
    bool IsOccupied,
    IReadOnlyList<PortOccupancy> Occupancies);

public sealed record PortTerminateResponse(
    int Port,
    int ProcessId,
    string ProcessName,
    string Message);
