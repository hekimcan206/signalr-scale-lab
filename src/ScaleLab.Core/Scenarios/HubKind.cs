namespace ScaleLab.Core.Scenarios;

/// <summary>Which hub the client connects to. Both run on every server; only the lifetime manager differs.</summary>
public enum HubKind
{
    /// <summary><c>/hubs/local</c>: the default in-memory lifetime manager. A send reaches this server's clients only.</summary>
    Local,

    /// <summary><c>/hubs/fanout</c>: the Redis backplane lifetime manager. A send reaches every server's clients.</summary>
    Redis,
}
