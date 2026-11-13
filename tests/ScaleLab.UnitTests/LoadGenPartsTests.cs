using System.Net;
using System.Text;
using ScaleLab.Hosting;
using ScaleLab.LoadGen;

namespace ScaleLab.UnitTests;

public sealed class LoadGenPartsTests
{
    [Fact]
    public void Command_line_reads_pairs_and_flags()
    {
        var options = CommandLine.Parse(["--scenario", "rr-skip", "--verbose", "--stages", "1,2"]);

        options["scenario"].Should().Be("rr-skip");
        options["verbose"].Should().Be("true");
        CommandLine.Get(options, "stages", "x").Should().Be("1,2");
        CommandLine.Int(options, "messages", 20).Should().Be(20);
        var stray = () => CommandLine.Parse(["oops"]);
        stray.Should().Throw<ArgumentException>();
        var missing = () => CommandLine.Required(options, "out");
        missing.Should().Throw<ArgumentException>().WithMessage("*--out*");
    }

    [Fact]
    public void Target_parses_the_server_list()
    {
        var target = CommandLine.Target(CommandLine.Parse(
            ["--direct", "http://a:1", "--proxy", "http://p:2", "--servers", "http://a:1, http://b:1", "--key", "k"]));

        target.Direct.Should().Be(new Uri("http://a:1"));
        target.Proxy.Should().Be(new Uri("http://p:2"));
        target.Servers.Should().Equal(new Uri("http://a:1"), new Uri("http://b:1"));
        target.Key.Should().Be("k");
    }

    [Fact]
    public void Failure_reasons_are_short_and_unwrap_aggregates()
    {
        var reason = ClientSwarm.Classify(new AggregateException(new HttpRequestException(new string('x', 500))));

        reason.Should().StartWith("HttpRequestException: ");
        reason.Length.Should().BeLessThanOrEqualTo("HttpRequestException: ".Length + 120);
        ClientSwarm.Classify(new InvalidOperationException("a\nb")).Should().Be("InvalidOperationException: a b");
    }

    [Fact]
    public async Task Cookie_handler_keeps_the_affinity_cookie_per_client_over_a_shared_pool()
    {
        var shared = new RecordingHandler();
        using var invoker = new HttpMessageInvoker(shared, disposeHandler: false);
        var cookies = new CookieContainer();
        var uri = new Uri("http://proxy:8080/sticky/hubs/fanout/negotiate");

        using (var client = new HttpClient(new CookieForwardingHandler(invoker, cookies)))
        {
            await client.PostAsync(uri, null, TestContext.Current.CancellationToken);
            await client.PostAsync(uri, null, TestContext.Current.CancellationToken);
        }

        shared.CookieHeaders.Should().Equal(null, ".ScaleLab.Affinity=abc");
        cookies.GetCookieHeader(new Uri("http://proxy:8080/sticky/hubs/fanout")).Should().Be(".ScaleLab.Affinity=abc");

        // Disposing one client's HttpClient must leave the shared pool usable for the next client.
        using var next = new HttpClient(new CookieForwardingHandler(invoker, new CookieContainer()));
        var response = await next.PostAsync(uri, null, TestContext.Current.CancellationToken);
        response.IsSuccessStatusCode.Should().BeTrue();
        shared.Disposed.Should().BeFalse();
    }

    [Fact]
    public void Lab_key_comparison()
    {
        var key = Encoding.UTF8.GetBytes("secret");
        LabHosting.KeyMatches(key, "secret").Should().BeTrue();
        LabHosting.KeyMatches(key, "Secret").Should().BeFalse();
        LabHosting.KeyMatches(key, "secret2").Should().BeFalse();
        LabHosting.KeyMatches(key, null).Should().BeFalse();
        LabHosting.KeyMatches(key, string.Empty).Should().BeFalse();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string?> CookieHeaders { get; } = [];

        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CookieHeaders.Add(request.Headers.TryGetValues("Cookie", out var v) ? string.Join("; ", v) : null);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("Set-Cookie", ".ScaleLab.Affinity=abc; path=/; httponly");
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
