using Microsoft.AspNetCore.SignalR;

namespace ScaleLab.HubServer;

/// <summary>
/// Broadcast hub whose lifetime manager is the Redis backplane when Redis is configured: a send on any server is
/// published to Redis and every server delivers it to its own connections.
/// </summary>
public sealed class FanoutHub(ConnectionTracker tracker) : Hub
{
    public const string Name = "fanout";

    public override Task OnConnectedAsync()
    {
        tracker.Opened(Name);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.Closed(Name);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Round-trip probe for interactive checks; returns its argument.</summary>
    public long Echo(long value) => value;
}

/// <summary>
/// The same hub with the default in-memory lifetime manager, even when Redis is configured. A send reaches only the
/// connections of the server that executes it: what a scaled-out app without a backplane does.
/// </summary>
public sealed class LocalHub(ConnectionTracker tracker) : Hub
{
    public const string Name = "local";

    public override Task OnConnectedAsync()
    {
        tracker.Opened(Name);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.Closed(Name);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Round-trip probe for interactive checks; returns its argument.</summary>
    public long Echo(long value) => value;
}
