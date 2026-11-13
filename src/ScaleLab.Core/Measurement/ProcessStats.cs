namespace ScaleLab.Core.Measurement;

/// <summary>
/// What a hub server or the proxy reports about itself at <c>GET /api/stats</c>.
/// </summary>
/// <param name="Node">Server name (container hostname or configured name).</param>
/// <param name="Role"><c>hub</c> or <c>proxy</c>.</param>
/// <param name="Connections">Live connections by kind: <c>local</c>/<c>fanout</c> hubs, or <c>websockets</c> at the proxy.</param>
/// <param name="WorkingSetBytes">Resident set size of the process.</param>
/// <param name="GcHeapBytes">Managed heap size after the last GC (after a forced full GC when requested).</param>
/// <param name="GcCommittedBytes">Memory the GC has committed from the OS.</param>
/// <param name="Backplane">Whether the fan-out hub is backed by Redis on this server.</param>
public sealed record ProcessStats(
    string Node,
    string Role,
    IReadOnlyDictionary<string, int> Connections,
    long WorkingSetBytes,
    long GcHeapBytes,
    long GcCommittedBytes,
    int ThreadCount,
    double CpuSeconds,
    bool Backplane)
{
    public int TotalConnections => Connections.Values.Sum();
}
