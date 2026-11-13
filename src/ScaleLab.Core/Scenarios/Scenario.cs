namespace ScaleLab.Core.Scenarios;

/// <summary>
/// One topology under test: how clients enter, which hub they use, and whether they skip the negotiate request.
/// </summary>
/// <param name="Name">Stable identifier used on the command line, in result files and in charts.</param>
/// <param name="Title">Short human label for tables and chart legends.</param>
public sealed record Scenario(
    string Name,
    string Title,
    Entry Entry,
    HubKind Hub,
    bool SkipNegotiation,
    string Explanation)
{
    /// <summary>Path of the hub on a hub server.</summary>
    public string HubPath => Hub == HubKind.Redis ? "/hubs/fanout" : "/hubs/local";

    /// <summary>The hub name the broadcast endpoint takes.</summary>
    public string HubName => Hub == HubKind.Redis ? "fanout" : "local";

    /// <summary>Builds the client URL for this scenario.</summary>
    /// <param name="directBase">Base URL of the first hub server, e.g. <c>http://hub-1:8080</c>.</param>
    /// <param name="proxyBase">Base URL of the proxy, e.g. <c>http://proxy:8080</c>.</param>
    public Uri ClientUrl(Uri directBase, Uri proxyBase)
    {
        ArgumentNullException.ThrowIfNull(directBase);
        ArgumentNullException.ThrowIfNull(proxyBase);
        return Entry switch
        {
            Entry.Direct => new Uri(directBase, HubPath),
            Entry.Sticky => new Uri(proxyBase, "/sticky" + HubPath),
            Entry.RoundRobin => new Uri(proxyBase, "/rr" + HubPath),
            _ => throw new InvalidOperationException($"Unknown entry {Entry}."),
        };
    }

    /// <summary>
    /// The share of connected clients a broadcast sent to one server should reach, when connections are spread over
    /// <paramref name="servers"/> servers. Without a backplane, only the clients on the server that sent it.
    /// </summary>
    public double ExpectedDeliveryRatio(int servers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(servers, 1);
        if (Entry == Entry.Direct || Hub == HubKind.Redis)
        {
            return 1.0;
        }

        return 1.0 / servers;
    }

    /// <summary>
    /// The share of connection attempts expected to succeed. A negotiated connection through a balancer without
    /// affinity succeeds only when the negotiate request and the WebSocket request land on the same server.
    /// </summary>
    public double ExpectedConnectRatio(int servers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(servers, 1);
        return Entry == Entry.RoundRobin && !SkipNegotiation ? 1.0 / servers : 1.0;
    }
}
