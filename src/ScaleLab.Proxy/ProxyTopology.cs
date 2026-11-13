using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;

namespace ScaleLab.Proxy;

/// <summary>
/// The proxy's routes and clusters, built in code from the list of hub servers.
/// </summary>
/// <remarks>
/// Both clusters point at the same servers; only affinity differs. That keeps every other variable (servers, network
/// path, proxy process) identical between the sticky and the non-sticky measurements.
/// </remarks>
public static class ProxyTopology
{
    public const string StickyCluster = "sticky";
    public const string RoundRobinCluster = "roundrobin";

    /// <summary>Name of the affinity cookie. HashCookie stores a hash of the destination id, not the address.</summary>
    public const string AffinityCookie = ".ScaleLab.Affinity";

    public static IReadOnlyList<RouteConfig> Routes() =>
    [
        Route("sticky", "/sticky", StickyCluster),
        Route("roundrobin", "/rr", RoundRobinCluster),
    ];

    /// <param name="destinations">Hub server base addresses, e.g. <c>http://hub-1:8080</c>.</param>
    public static IReadOnlyList<ClusterConfig> Clusters(IReadOnlyList<string> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        if (destinations.Count == 0)
        {
            throw new ArgumentException("At least one hub server address is required.", nameof(destinations));
        }

        var map = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < destinations.Count; i++)
        {
            if (!Uri.TryCreate(destinations[i], UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"'{destinations[i]}' is not an absolute http(s) URL.", nameof(destinations));
            }

            map[$"hub-{i + 1}"] = new DestinationConfig { Address = uri.ToString() };
        }

        // WebSockets are long-lived; YARP applies the activity timeout to them as idle time. SignalR's 15-second
        // keep-alive pings keep a healthy connection well inside two minutes.
        var request = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(2) };

        return
        [
            new ClusterConfig
            {
                ClusterId = StickyCluster,
                LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                SessionAffinity = new SessionAffinityConfig
                {
                    Enabled = true,
                    Policy = "HashCookie",

                    // A server that went away: send the client somewhere else and let SignalR fail the stale
                    // connection id, rather than returning 503 forever for that cookie.
                    FailurePolicy = "Redistribute",
                    AffinityKeyName = AffinityCookie,
                    Cookie = new SessionAffinityCookieConfig { HttpOnly = true, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax },
                },
                HttpRequest = request,
                Destinations = map,
            },
            new ClusterConfig
            {
                ClusterId = RoundRobinCluster,
                LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                HttpRequest = request,
                Destinations = map,
            },
        ];
    }

    private static RouteConfig Route(string id, string prefix, string cluster) => new()
    {
        RouteId = id,
        ClusterId = cluster,
        Match = new RouteMatch { Path = prefix + "/{**catch-all}" },
        Transforms = [new Dictionary<string, string> { ["PathRemovePrefix"] = prefix }],
    };
}
