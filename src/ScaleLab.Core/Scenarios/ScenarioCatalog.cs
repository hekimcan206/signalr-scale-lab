namespace ScaleLab.Core.Scenarios;

/// <summary>The five topologies the lab compares.</summary>
public static class ScenarioCatalog
{
    public static readonly Scenario SingleNode = new(
        "single",
        "single node",
        Entry.Direct,
        HubKind.Local,
        SkipNegotiation: false,
        "One hub server, no proxy, in-memory hub. The baseline for memory and latency.");

    public static readonly Scenario StickyOnly = new(
        "sticky-only",
        "sticky, no backplane",
        Entry.Sticky,
        HubKind.Local,
        SkipNegotiation: false,
        "Proxy with cookie affinity in front of three servers, in-memory hub. Connections work; broadcasts reach only the sending server's clients.");

    public static readonly Scenario StickyRedis = new(
        "sticky-redis",
        "sticky + Redis",
        Entry.Sticky,
        HubKind.Redis,
        SkipNegotiation: false,
        "Proxy with cookie affinity, Redis backplane. The configuration Microsoft documents for scale-out.");

    public static readonly Scenario RoundRobinNegotiate = new(
        "rr-negotiate",
        "round-robin, negotiate",
        Entry.RoundRobin,
        HubKind.Redis,
        SkipNegotiation: false,
        "Proxy without affinity, Redis backplane, normal negotiate. The WebSocket request often lands on a server that never saw the negotiate.");

    public static readonly Scenario RoundRobinSkip = new(
        "rr-skip",
        "round-robin, skip negotiate + Redis",
        Entry.RoundRobin,
        HubKind.Redis,
        SkipNegotiation: true,
        "Proxy without affinity, Redis backplane, WebSockets only with SkipNegotiation: one HTTP request per connection, so affinity is not needed.");

    public static IReadOnlyList<Scenario> All { get; } =
        [SingleNode, StickyOnly, StickyRedis, RoundRobinNegotiate, RoundRobinSkip];

    /// <summary>Finds a scenario by its <see cref="Scenario.Name"/>, case-insensitively.</summary>
    /// <exception cref="ArgumentException">The name is not in the catalogue.</exception>
    public static Scenario Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown scenario '{name}'. Known: {string.Join(", ", All.Select(s => s.Name))}.", nameof(name));
    }
}
