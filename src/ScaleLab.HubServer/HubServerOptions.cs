namespace ScaleLab.HubServer;

/// <summary>Configuration section <c>Lab</c>.</summary>
public sealed class HubServerOptions
{
    public const string Section = "Lab";

    /// <summary>StackExchange.Redis configuration string. Empty: no backplane, both hubs are in-memory.</summary>
    public string? Redis { get; set; }

    /// <summary>
    /// Prefix for the backplane's Redis channels, so several apps can share one Redis without receiving each other's
    /// messages.
    /// </summary>
    public string ChannelPrefix { get; set; } = "scalelab";

    /// <summary>Key required in <c>X-Lab-Key</c> for <c>/api</c>. Empty: open (local demo only).</summary>
    public string? Key { get; set; }

    /// <summary>Largest broadcast payload <c>/api/broadcast</c> accepts, in bytes.</summary>
    public int MaxPayloadBytes { get; set; } = 4096;
}
