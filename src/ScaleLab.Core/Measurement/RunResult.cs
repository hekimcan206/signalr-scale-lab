namespace ScaleLab.Core.Measurement;

/// <summary>One benchmark run: one scenario ramped through its stages, as written to <c>benchmark-results/runs</c>.</summary>
public sealed record RunResult(
    string Scenario,
    int Repetition,
    DateTimeOffset StartedAt,
    RunSettings Settings,
    MemorySnapshot Baseline,
    IReadOnlyList<StageResult> Stages);

/// <param name="Servers">Hub servers behind the proxy.</param>
/// <param name="MessagesPerStage">Broadcasts sent at each stage plateau.</param>
/// <param name="MessagesPerSecond">Broadcast rate.</param>
/// <param name="PayloadBytes">Size of the string payload in each broadcast.</param>
/// <param name="ConnectConcurrency">Connection attempts in flight during a ramp.</param>
/// <param name="Host">Free-text description of where the run happened.</param>
public sealed record RunSettings(
    int Servers,
    int MessagesPerStage,
    double MessagesPerSecond,
    int PayloadBytes,
    int ConnectConcurrency,
    string Host);

/// <summary>Memory of every process in the topology at one moment, after a forced full GC.</summary>
public sealed record MemorySnapshot(
    long ServerWorkingSetBytes,
    long ServerHeapBytes,
    long ProxyWorkingSetBytes,
    long ProxyHeapBytes,
    long ClientWorkingSetBytes,
    long ClientHeapBytes,
    IReadOnlyList<ProcessStats> Processes);

/// <param name="Target">Connection attempts made up to and including this stage.</param>
/// <param name="Connected">Connections open when the stage was measured.</param>
/// <param name="Failed">Attempts that never connected.</param>
/// <param name="Dropped">Connections that closed after connecting, up to this stage.</param>
/// <param name="FailureReasons">Failed attempts by reason.</param>
/// <param name="RampSeconds">Wall time to make this stage's new attempts.</param>
/// <param name="ConnectionsPerServer">Connections each hub server reported for the hub under test.</param>
/// <param name="CpuCoresDuringFanout">Average CPU cores each process used while broadcasts were sent and drained (diagnostic: shows which side saturates).</param>
public sealed record StageResult(
    int Target,
    int Connected,
    int Failed,
    int Dropped,
    IReadOnlyDictionary<string, int> FailureReasons,
    double RampSeconds,
    IReadOnlyDictionary<string, int> ConnectionsPerServer,
    MemorySnapshot Memory,
    FanoutSummary Fanout,
    IReadOnlyDictionary<string, double>? CpuCoresDuringFanout = null);
