using System.Diagnostics.Metrics;

namespace ScaleLab.HubServer;

/// <summary>Live connection counts per hub, for <c>/api/stats</c> and as an OpenTelemetry gauge.</summary>
public sealed class ConnectionTracker : IDisposable
{
    public const string MeterName = "ScaleLab.Hub";

    private readonly Meter _meter;
    private int _fanout;
    private int _local;

    public ConnectionTracker(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);
        _meter.CreateObservableUpDownCounter(
            "scalelab.hub.connections",
            () => new[]
            {
                new Measurement<int>(Volatile.Read(ref _fanout), new KeyValuePair<string, object?>("hub", FanoutHub.Name)),
                new Measurement<int>(Volatile.Read(ref _local), new KeyValuePair<string, object?>("hub", LocalHub.Name)),
            },
            unit: "{connection}",
            description: "Connections currently open, by hub.");
    }

    public void Opened(string hub) => Interlocked.Increment(ref Counter(hub));

    public void Closed(string hub) => Interlocked.Decrement(ref Counter(hub));

    public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>
    {
        [FanoutHub.Name] = Volatile.Read(ref _fanout),
        [LocalHub.Name] = Volatile.Read(ref _local),
    };

    public void Dispose() => _meter.Dispose();

    private ref int Counter(string hub)
    {
        if (hub == FanoutHub.Name)
        {
            return ref _fanout;
        }

        if (hub == LocalHub.Name)
        {
            return ref _local;
        }

        throw new ArgumentOutOfRangeException(nameof(hub), hub, "Unknown hub.");
    }
}
