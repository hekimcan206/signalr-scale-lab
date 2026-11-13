namespace ScaleLab.Core.Measurement;

/// <summary>
/// Counts who received which broadcast, and when, without locks on the receive path.
/// </summary>
/// <remarks>
/// Ten thousand client callbacks fire within milliseconds of each other for every broadcast, so recording must not
/// serialise them: each receive is one interlocked increment and one array write. All timestamps come from
/// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> in the same process, so send and receive times share one
/// monotonic clock and no clock synchronisation between machines is involved.
/// </remarks>
public sealed class FanoutLedger
{
    private readonly long[] _sentAt;
    private readonly int[] _expected;
    private readonly int[] _received;
    private readonly long[][] _receivedAt;
    private readonly int _capacity;
    private int _strays;

    /// <param name="messages">Number of broadcasts in this measurement; sequence numbers are 0..messages-1.</param>
    /// <param name="receiversCapacity">Upper bound of receivers per broadcast (the connection count).</param>
    public FanoutLedger(int messages, int receiversCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(messages, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(receiversCapacity);
        _capacity = receiversCapacity;
        _sentAt = new long[messages];
        _expected = new int[messages];
        _received = new int[messages];
        _receivedAt = new long[messages][];
        for (var i = 0; i < messages; i++)
        {
            _receivedAt[i] = new long[receiversCapacity];
        }
    }

    public int Messages => _sentAt.Length;

    /// <summary>Receives that could not be attributed: unknown sequence number or more receivers than capacity.</summary>
    public int Strays => Volatile.Read(ref _strays);

    /// <summary>Total receives recorded so far, capped per broadcast at its expected count.</summary>
    public long TotalReceived
    {
        get
        {
            long sum = 0;
            for (var i = 0; i < _received.Length; i++)
            {
                sum += Math.Min(Volatile.Read(ref _received[i]), Volatile.Read(ref _expected[i]));
            }

            return sum;
        }
    }

    /// <summary>Total receives expected for the broadcasts sent so far.</summary>
    public long TotalExpected
    {
        get
        {
            long sum = 0;
            for (var i = 0; i < _expected.Length; i++)
            {
                sum += Volatile.Read(ref _expected[i]);
            }

            return sum;
        }
    }

    /// <summary>
    /// Records that broadcast <paramref name="sequence"/> is about to be sent. Call before the send, so that a receive
    /// racing the send's completion still finds its start time.
    /// </summary>
    public void MarkSent(int sequence, long timestamp, int expectedReceivers)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedReceivers);
        _sentAt[sequence] = timestamp;
        Volatile.Write(ref _expected[sequence], expectedReceivers);
    }

    /// <summary>Records one client receiving broadcast <paramref name="sequence"/>. Safe to call concurrently.</summary>
    public void Record(long sequence, long timestamp)
    {
        if (sequence < 0 || sequence >= _sentAt.Length)
        {
            Interlocked.Increment(ref _strays);
            return;
        }

        var seq = (int)sequence;
        var slot = Interlocked.Increment(ref _received[seq]) - 1;
        if (slot >= _capacity)
        {
            Interlocked.Increment(ref _strays);
            return;
        }

        _receivedAt[seq][slot] = timestamp;
    }

    /// <summary>Summarises the broadcasts marked as sent.</summary>
    /// <param name="ticksPerSecond">The timestamp frequency, normally <see cref="System.Diagnostics.Stopwatch.Frequency"/>.</param>
    public FanoutSummary Summarize(long ticksPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerSecond, 1);
        var toMs = 1000.0 / ticksPerSecond;
        var latencies = new List<double>();
        var completions = new List<double>();
        long expected = 0, received = 0;
        var sent = 0;
        var complete = 0;

        for (var i = 0; i < _sentAt.Length; i++)
        {
            var exp = Volatile.Read(ref _expected[i]);
            if (_sentAt[i] == 0 && exp == 0)
            {
                continue;
            }

            sent++;
            var got = Math.Min(Volatile.Read(ref _received[i]), _capacity);
            expected += exp;
            received += Math.Min(got, exp);
            long last = 0;
            for (var r = 0; r < got; r++)
            {
                var at = _receivedAt[i][r];
                latencies.Add((at - _sentAt[i]) * toMs);
                last = Math.Max(last, at);
            }

            // Fan-out completion is only meaningful when every expected client got the message.
            if (exp > 0 && got >= exp)
            {
                complete++;
                completions.Add((last - _sentAt[i]) * toMs);
            }
        }

        return new FanoutSummary(
            sent,
            complete,
            expected,
            received,
            expected == 0 ? 0 : (double)received / expected,
            LatencySummary.From(latencies),
            LatencySummary.From(completions),
            Strays);
    }
}

/// <param name="MessagesSent">Broadcasts sent.</param>
/// <param name="MessagesComplete">Broadcasts that reached every client connected when they were sent.</param>
/// <param name="ExpectedReceives">Sum over broadcasts of the connected clients at send time.</param>
/// <param name="Receives">Receives counted against that expectation (capped per broadcast).</param>
/// <param name="DeliveryRatio">Receives / expected receives.</param>
/// <param name="LatencyMs">Per receive: send to arrival at one client.</param>
/// <param name="CompletionMs">Per complete broadcast: send to arrival at the last client.</param>
/// <param name="Strays">Unattributable receives (should be zero).</param>
public sealed record FanoutSummary(
    int MessagesSent,
    int MessagesComplete,
    long ExpectedReceives,
    long Receives,
    double DeliveryRatio,
    LatencySummary LatencyMs,
    LatencySummary CompletionMs,
    int Strays);
