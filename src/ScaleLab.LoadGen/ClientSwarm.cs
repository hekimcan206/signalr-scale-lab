using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using ScaleLab.Core.Measurement;

namespace ScaleLab.LoadGen;

/// <summary>
/// Many SignalR client connections to one URL, opened in bounded-concurrency ramps, all recording broadcasts into the
/// current <see cref="FanoutLedger"/>.
/// </summary>
internal sealed class ClientSwarm : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri _url;
    private readonly bool _skipNegotiation;
    private readonly HttpMessageInvoker _shared;
    private readonly ConcurrentBag<HubConnection> _connections = [];
    private readonly ConcurrentDictionary<string, int> _failures = new();
    private FanoutLedger? _ledger;
    private int _attempted;
    private int _connected;
    private int _dropped;
    private volatile bool _closing;

    public ClientSwarm(Uri url, bool skipNegotiation)
    {
        _url = url;
        _skipNegotiation = skipNegotiation;

        // One pool for every client's negotiate request (see CookieForwardingHandler). Cookies are per client.
        _shared = new HttpMessageInvoker(
            new SocketsHttpHandler
            {
                UseCookies = false,
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
                ConnectTimeout = ConnectTimeout,
            },
            disposeHandler: true);
    }

    public int Attempted => Volatile.Read(ref _attempted);

    /// <summary>Connections that are open now.</summary>
    public int Connected => Volatile.Read(ref _connected);

    /// <summary>Connections that closed after they had connected, without the swarm closing them.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    public int Failed => _failures.Values.Sum();

    public IReadOnlyDictionary<string, int> FailureReasons => new SortedDictionary<string, int>(_failures, StringComparer.Ordinal);

    /// <summary>Directs every subsequent receive into <paramref name="ledger"/>.</summary>
    public void UseLedger(FanoutLedger? ledger) => Volatile.Write(ref _ledger, ledger);

    /// <summary>Opens <paramref name="count"/> more connections, at most <paramref name="concurrency"/> at a time.</summary>
    /// <returns>Wall time of the ramp.</returns>
    public async Task<TimeSpan> OpenAsync(int count, int concurrency, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
            async (_, token) => await OpenOneAsync(token));
        return Stopwatch.GetElapsedTime(started);
    }

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        await Parallel.ForEachAsync(
            _connections,
            new ParallelOptions { MaxDegreeOfParallelism = 256 },
            async (connection, _) =>
            {
                try
                {
                    await connection.DisposeAsync();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Shutting down: a connection that fails to close cleanly is already gone from the server's view.
                }
            });
        _connections.Clear();
        _shared.Dispose();
    }

    internal static string Classify(Exception ex)
    {
        var inner = ex is AggregateException { InnerException: { } first } ? first : ex;
        var message = inner.Message.ReplaceLineEndings(" ");
        if (message.Length > 120)
        {
            message = message[..120];
        }

        return $"{inner.GetType().Name}: {message}";
    }

    private async Task OpenOneAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _attempted);
        var connection = new HubConnectionBuilder()
            .WithUrl(_url, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = _skipNegotiation;
                var cookies = options.Cookies;
                options.HttpMessageHandlerFactory = _ => new CookieForwardingHandler(_shared, cookies);
            })
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.None))
            .Build();

        connection.On<long, string>("tick", (sequence, _) =>
            Volatile.Read(ref _ledger)?.Record(sequence, Stopwatch.GetTimestamp()));

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await connection.StartAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _failures.AddOrUpdate(Classify(ex), 1, (_, n) => n + 1);
            await connection.DisposeAsync();
            return;
        }

        connection.Closed += _ =>
        {
            Interlocked.Decrement(ref _connected);
            if (!_closing)
            {
                Interlocked.Increment(ref _dropped);
            }

            return Task.CompletedTask;
        };
        Interlocked.Increment(ref _connected);
        _connections.Add(connection);
    }

    /// <summary>Process memory of the load generator itself, after a forced full GC, for client-side cost.</summary>
    public static (long WorkingSet, long Heap) SampleSelf()
    {
        var stats = ProcessSampler.Sample("loadgen", "client", new Dictionary<string, int>(), backplane: false, forceFullGc: true);
        return (stats.WorkingSetBytes, stats.GcHeapBytes);
    }
}
