using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using ScaleLab.Core.Scenarios;
using ScaleLab.LoadGen;

namespace ScaleLab.IntegrationTests;

/// <summary>What the backplane and the proxy's affinity do, on real sockets with a real Redis.</summary>
[Collection(LabGroup.Name)]
public sealed class ScaleOutTests(LabTopology lab)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Redis_backed_hub_delivers_a_broadcast_sent_on_one_server_to_clients_on_both()
    {
        await using var onA = await Clients.ConnectAsync(new Uri(lab.HubA, "/hubs/fanout"), 4);
        await using var onB = await Clients.ConnectAsync(new Uri(lab.HubB, "/hubs/fanout"), 4);

        using var api = LabApi.Create(LabTopology.Key);
        await api.BroadcastAsync(lab.HubA, "fanout", 7, 16, TestContext.Current.CancellationToken);

        await onA.WaitForAsync(7, Wait);
        await onB.WaitForAsync(7, Wait);
        onA.ReceivedBy(7).Should().Be(4);
        onB.ReceivedBy(7).Should().Be(4, "the backplane carries the send from hub A to hub B's clients");
    }

    [Fact]
    public async Task In_memory_hub_on_the_same_servers_reaches_only_the_sending_servers_clients()
    {
        await using var onA = await Clients.ConnectAsync(new Uri(lab.HubA, "/hubs/local"), 4);
        await using var onB = await Clients.ConnectAsync(new Uri(lab.HubB, "/hubs/local"), 4);

        using var api = LabApi.Create(LabTopology.Key);
        await api.BroadcastAsync(lab.HubA, "local", 11, 16, TestContext.Current.CancellationToken);

        await onA.WaitForAsync(11, Wait);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        onA.ReceivedBy(11).Should().Be(4);
        onB.ReceivedBy(11).Should().Be(0, "LocalHub keeps the in-memory lifetime manager although Redis is configured");
    }

    [Fact]
    public async Task Sticky_route_connects_every_negotiating_client_and_spreads_them_over_both_servers()
    {
        var target = lab.Target;
        await using var swarm = new ClientSwarm(ScenarioCatalog.StickyRedis.ClientUrl(target.Direct, target.Proxy), skipNegotiation: false);
        await swarm.OpenAsync(20, 4, TestContext.Current.CancellationToken);

        swarm.Connected.Should().Be(20);
        swarm.Failed.Should().Be(0);

        using var api = LabApi.Create(LabTopology.Key);
        var a = await api.StatsAsync(lab.HubA, forceGc: false, TestContext.Current.CancellationToken);
        var b = await api.StatsAsync(lab.HubB, forceGc: false, TestContext.Current.CancellationToken);
        a.Connections["fanout"].Should().BeGreaterThan(0);
        b.Connections["fanout"].Should().BeGreaterThan(0);
        (a.Connections["fanout"] + b.Connections["fanout"]).Should().BeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public async Task Round_robin_without_affinity_sends_the_websocket_to_the_server_that_did_not_negotiate()
    {
        var target = lab.Target;
        await using var swarm = new ClientSwarm(ScenarioCatalog.RoundRobinNegotiate.ClientUrl(target.Direct, target.Proxy), skipNegotiation: false);

        // One at a time: with two servers, strict round-robin sends every negotiate to one server and the WebSocket
        // that follows it to the other, so every attempt fails, deterministically.
        await swarm.OpenAsync(10, 1, TestContext.Current.CancellationToken);

        swarm.Connected.Should().Be(0);
        swarm.Failed.Should().Be(10);
        swarm.FailureReasons.Keys.Should().ContainSingle().Which.Should().Contain("404");
    }

    [Fact]
    public async Task Skipping_negotiation_makes_affinity_unnecessary_and_the_backplane_reaches_everyone()
    {
        var target = lab.Target;
        await using var swarm = new ClientSwarm(ScenarioCatalog.RoundRobinSkip.ClientUrl(target.Direct, target.Proxy), skipNegotiation: true);
        await swarm.OpenAsync(20, 4, TestContext.Current.CancellationToken);
        swarm.Connected.Should().Be(20);

        using var api = LabApi.Create(LabTopology.Key);
        var fanout = await FanoutProbe.MeasureAsync(swarm, api, lab.HubA, "fanout", 3, 20, 32, Wait, TestContext.Current.CancellationToken);

        fanout.DeliveryRatio.Should().Be(1.0);
        fanout.MessagesComplete.Should().Be(3);
        fanout.LatencyMs.Count.Should().Be(60);
        fanout.Strays.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_backplane_a_sticky_deployment_delivers_about_half_the_broadcasts_on_two_servers()
    {
        using var api = LabApi.Create(LabTopology.Key);
        var outcome = await DemoRunner.RunScenarioAsync(lab.Target, api, ScenarioCatalog.StickyOnly, 30, 2, TestContext.Current.CancellationToken);

        outcome.Connected.Should().Be(30);
        outcome.DeliveryRatio.Should().BeInRange(0.25, 0.75);
        outcome.AsExpected.Should().BeTrue(outcome.Expectation);
    }

    [Fact]
    public async Task Proxy_counts_the_websockets_it_is_carrying()
    {
        var target = lab.Target;
        await using var swarm = new ClientSwarm(ScenarioCatalog.RoundRobinSkip.ClientUrl(target.Direct, target.Proxy), skipNegotiation: true);
        await swarm.OpenAsync(6, 3, TestContext.Current.CancellationToken);

        using var api = LabApi.Create(LabTopology.Key);
        var proxy = await api.StatsAsync(lab.Proxy, forceGc: true, TestContext.Current.CancellationToken);

        proxy.Role.Should().Be("proxy");
        proxy.Connections["websockets"].Should().BeGreaterThanOrEqualTo(6);
        proxy.WorkingSetBytes.Should().BePositive();
    }

    private sealed class Clients : IAsyncDisposable
    {
        private readonly List<HubConnection> _connections = [];
        private readonly Dictionary<long, int> _received = [];
        private readonly Lock _gate = new();

        public static async Task<Clients> ConnectAsync(Uri url, int count)
        {
            var clients = new Clients();
            for (var i = 0; i < count; i++)
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl(url, o => o.Transports = HttpTransportType.WebSockets)
                    .Build();
                connection.On<long, string>("tick", (seq, _) =>
                {
                    lock (clients._gate)
                    {
                        clients._received[seq] = clients._received.GetValueOrDefault(seq) + 1;
                    }
                });
                await connection.StartAsync();
                clients._connections.Add(connection);
            }

            return clients;
        }

        public int ReceivedBy(long seq)
        {
            lock (_gate)
            {
                return _received.GetValueOrDefault(seq);
            }
        }

        public async Task WaitForAsync(long seq, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (ReceivedBy(seq) < _connections.Count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var c in _connections)
            {
                await c.DisposeAsync();
            }
        }
    }
}

/// <summary>The servers' HTTP surface: the lab key, input validation, stats.</summary>
[Collection(LabGroup.Name)]
public sealed class ApiTests(LabTopology lab)
{
    [Fact]
    public async Task Api_rejects_requests_without_the_right_key()
    {
        using var http = new HttpClient();
        var missing = await http.GetAsync(new Uri(lab.HubA, "/api/stats"), TestContext.Current.CancellationToken);
        using var wrong = new HttpRequestMessage(HttpMethod.Post, new Uri(lab.HubA, "/api/broadcast/fanout?seq=1"));
        wrong.Headers.Add("X-Lab-Key", "not-the-key");
        var wrongKey = await http.SendAsync(wrong, TestContext.Current.CancellationToken);
        var proxy = await http.GetAsync(new Uri(lab.Proxy, "/api/stats"), TestContext.Current.CancellationToken);

        missing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        proxy.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/broadcast/nope?seq=1", HttpStatusCode.NotFound)]
    [InlineData("/api/broadcast/fanout?seq=1&bytes=5000", HttpStatusCode.BadRequest)]
    [InlineData("/api/broadcast/fanout?seq=1&bytes=-1", HttpStatusCode.BadRequest)]
    [InlineData("/api/broadcast/local?seq=1&bytes=4096", HttpStatusCode.Accepted)]
    public async Task Broadcast_validates_hub_and_payload_size(string path, HttpStatusCode expected)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(lab.HubA, path));
        request.Headers.Add("X-Lab-Key", LabTopology.Key);
        var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Stats_report_node_backplane_and_memory()
    {
        using var api = LabApi.Create(LabTopology.Key);
        var stats = await api.StatsAsync(lab.HubB, forceGc: true, TestContext.Current.CancellationToken);

        stats.Node.Should().Be("hub-b");
        stats.Role.Should().Be("hub");
        stats.Backplane.Should().BeTrue();
        stats.Connections.Keys.Should().BeEquivalentTo(["fanout", "local"]);
        stats.GcHeapBytes.Should().BePositive();
    }

    [Fact]
    public async Task A_server_without_Redis_has_no_backplane_and_an_open_api_when_no_key_is_set()
    {
        var standalone = await lab.StartStandaloneHubAsync("solo");
        using var api = LabApi.Create(key: null);
        var stats = await api.StatsAsync(standalone, forceGc: false, TestContext.Current.CancellationToken);

        stats.Backplane.Should().BeFalse();
        stats.Node.Should().Be("solo");
    }

    [Fact]
    public async Task Health_endpoints_answer()
    {
        using var http = new HttpClient();
        (await http.GetAsync(new Uri(lab.HubA, "/healthz"), TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
        (await http.GetAsync(new Uri(lab.Proxy, "/healthz"), TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();
    }
}

/// <summary>The load generator's bench run, end to end, at a tiny scale.</summary>
[Collection(LabGroup.Name)]
public sealed class BenchRunnerTests(LabTopology lab)
{
    [Fact]
    public async Task A_bench_run_ramps_through_its_stages_and_records_memory_and_fanout()
    {
        var options = new BenchOptions(
            ScenarioCatalog.StickyRedis,
            [10, 24],
            Messages: 4,
            MessagesPerSecond: 20,
            PayloadBytes: 32,
            Concurrency: 8,
            Settle: TimeSpan.FromMilliseconds(200),
            Drain: TimeSpan.FromSeconds(5),
            Repetition: 1,
            Host: "integration test");

        var result = await BenchRunner.RunAsync(lab.Target, options, TextWriter.Null, TestContext.Current.CancellationToken);

        result.Scenario.Should().Be("sticky-redis");
        result.Stages.Select(s => s.Target).Should().Equal(10, 24);
        result.Stages.Select(s => s.Connected).Should().Equal(10, 24);
        result.Stages.Should().AllSatisfy(s =>
        {
            s.Failed.Should().Be(0);
            s.Fanout.DeliveryRatio.Should().Be(1.0);
            s.Fanout.MessagesSent.Should().Be(4);
            s.Memory.Processes.Should().HaveCount(3);
            s.Memory.ServerWorkingSetBytes.Should().BePositive();
        });
        result.Stages[^1].ConnectionsPerServer.Values.Sum().Should().Be(24);
        result.Baseline.Processes.Should().HaveCount(3);
    }
}
