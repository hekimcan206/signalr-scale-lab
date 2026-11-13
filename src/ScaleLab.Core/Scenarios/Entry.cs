namespace ScaleLab.Core.Scenarios;

/// <summary>How a client reaches the hub servers.</summary>
public enum Entry
{
    /// <summary>Straight to the first hub server, no proxy: the single-node baseline.</summary>
    Direct,

    /// <summary>Through the proxy's <c>/sticky</c> route: round-robin with cookie session affinity.</summary>
    Sticky,

    /// <summary>Through the proxy's <c>/rr</c> route: plain round-robin, every request balanced independently.</summary>
    RoundRobin,
}
