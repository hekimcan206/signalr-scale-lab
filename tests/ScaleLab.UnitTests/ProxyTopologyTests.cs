using ScaleLab.Proxy;

namespace ScaleLab.UnitTests;

public sealed class ProxyTopologyTests
{
    private static readonly string[] Hubs = ["http://hub-1:8080", "http://hub-2:8080", "http://hub-3:8080"];

    [Fact]
    public void Only_the_sticky_cluster_has_session_affinity()
    {
        var clusters = ProxyTopology.Clusters(Hubs).ToDictionary(c => c.ClusterId);

        var sticky = clusters[ProxyTopology.StickyCluster];
        sticky.SessionAffinity!.Enabled.Should().BeTrue();
        sticky.SessionAffinity.Policy.Should().Be("HashCookie");
        sticky.SessionAffinity.AffinityKeyName.Should().Be(ProxyTopology.AffinityCookie);
        sticky.SessionAffinity.Cookie!.HttpOnly.Should().BeTrue();

        clusters[ProxyTopology.RoundRobinCluster].SessionAffinity.Should().BeNull();
    }

    [Fact]
    public void Both_clusters_balance_round_robin_over_the_same_servers()
    {
        var clusters = ProxyTopology.Clusters(Hubs);

        clusters.Should().HaveCount(2);
        clusters.Should().AllSatisfy(c =>
        {
            c.LoadBalancingPolicy.Should().Be("RoundRobin");
            c.Destinations!.Keys.Should().Equal("hub-1", "hub-2", "hub-3");
            c.Destinations.Values.Select(d => d.Address).Should().Equal("http://hub-1:8080/", "http://hub-2:8080/", "http://hub-3:8080/");
            c.HttpRequest!.ActivityTimeout.Should().Be(TimeSpan.FromMinutes(2));
        });
    }

    [Fact]
    public void Routes_strip_their_prefix_before_forwarding()
    {
        var routes = ProxyTopology.Routes().ToDictionary(r => r.RouteId);

        routes["sticky"].Match.Path.Should().Be("/sticky/{**catch-all}");
        routes["sticky"].ClusterId.Should().Be(ProxyTopology.StickyCluster);
        routes["sticky"].Transforms!.Single()["PathRemovePrefix"].Should().Be("/sticky");
        routes["roundrobin"].Match.Path.Should().Be("/rr/{**catch-all}");
        routes["roundrobin"].ClusterId.Should().Be(ProxyTopology.RoundRobinCluster);
    }

    [Theory]
    [InlineData("hub-1:8080")]
    [InlineData("ftp://hub-1")]
    [InlineData("/relative")]
    public void Invalid_destinations_are_rejected_at_startup(string address)
    {
        var act = () => ProxyTopology.Clusters([address]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void At_least_one_destination_is_required()
    {
        var act = () => ProxyTopology.Clusters([]);
        act.Should().Throw<ArgumentException>();
    }
}
