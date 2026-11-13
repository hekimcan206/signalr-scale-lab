using ScaleLab.Core.Scenarios;
using ScaleLab.LoadGen;

namespace ScaleLab.UnitTests;

public sealed class ScenarioTests
{
    private static readonly Uri Direct = new("http://hub-1:8080");
    private static readonly Uri Proxy = new("http://proxy:8080");

    [Theory]
    [InlineData("single", "http://hub-1:8080/hubs/local")]
    [InlineData("sticky-only", "http://proxy:8080/sticky/hubs/local")]
    [InlineData("sticky-redis", "http://proxy:8080/sticky/hubs/fanout")]
    [InlineData("rr-negotiate", "http://proxy:8080/rr/hubs/fanout")]
    [InlineData("rr-skip", "http://proxy:8080/rr/hubs/fanout")]
    public void Each_scenario_connects_to_its_route_and_hub(string name, string url)
    {
        ScenarioCatalog.Get(name).ClientUrl(Direct, Proxy).Should().Be(new Uri(url));
    }

    [Fact]
    public void Catalog_names_are_unique_and_lookup_ignores_case()
    {
        ScenarioCatalog.All.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        ScenarioCatalog.Get("STICKY-REDIS").Should().BeSameAs(ScenarioCatalog.StickyRedis);
        var act = () => ScenarioCatalog.Get("nope");
        act.Should().Throw<ArgumentException>().WithMessage("*sticky-redis*");
    }

    [Theory]
    [InlineData("single", 3, 1.0, 1.0)]
    [InlineData("sticky-only", 3, 1.0, 1 / 3.0)]
    [InlineData("sticky-redis", 3, 1.0, 1.0)]
    [InlineData("rr-negotiate", 3, 1 / 3.0, 1.0)]
    [InlineData("rr-skip", 3, 1.0, 1.0)]
    [InlineData("sticky-only", 1, 1.0, 1.0)]
    public void Expected_behaviour_per_topology(string name, int servers, double connect, double delivery)
    {
        var scenario = ScenarioCatalog.Get(name);
        scenario.ExpectedConnectRatio(servers).Should().BeApproximately(connect, 1e-12);
        scenario.ExpectedDeliveryRatio(servers).Should().BeApproximately(delivery, 1e-12);
    }

    [Theory]
    [InlineData("single", 1.0, 1.0, true)]
    [InlineData("single", 0.99, 1.0, false)]
    [InlineData("sticky-only", 1.0, 0.35, true)]
    [InlineData("sticky-only", 1.0, 1.0, false)]
    [InlineData("sticky-redis", 1.0, 0.999, false)]
    [InlineData("rr-negotiate", 0.31, 1.0, true)]
    [InlineData("rr-negotiate", 1.0, 1.0, false)]
    [InlineData("rr-skip", 1.0, 1.0, true)]
    public void Demo_judges_outcomes_against_the_topology(string name, double connect, double delivery, bool ok)
    {
        DemoRunner.Judge(ScenarioCatalog.Get(name), 3, connect, delivery).Ok.Should().Be(ok);
    }
}
