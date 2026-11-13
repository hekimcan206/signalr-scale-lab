using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using ScaleLab.Core.Measurement;

namespace ScaleLab.UnitTests;

public sealed class FanoutLedgerTests
{
    // 1 tick = 1 ms keeps the arithmetic readable.
    private const long Frequency = 1000;

    [Fact]
    public void Full_delivery_gives_ratio_one_and_completion_at_the_last_receive()
    {
        var ledger = new FanoutLedger(2, 3);
        ledger.MarkSent(0, 1000, 3);
        ledger.Record(0, 1002);
        ledger.Record(0, 1005);
        ledger.Record(0, 1010);
        ledger.MarkSent(1, 2000, 3);
        ledger.Record(1, 2001);
        ledger.Record(1, 2001);
        ledger.Record(1, 2004);

        var summary = ledger.Summarize(Frequency);

        summary.MessagesSent.Should().Be(2);
        summary.MessagesComplete.Should().Be(2);
        summary.ExpectedReceives.Should().Be(6);
        summary.Receives.Should().Be(6);
        summary.DeliveryRatio.Should().Be(1.0);
        summary.LatencyMs.Count.Should().Be(6);
        summary.LatencyMs.Max.Should().Be(10);
        summary.CompletionMs.Count.Should().Be(2);
        summary.CompletionMs.Max.Should().Be(10);
        summary.CompletionMs.P50.Should().Be(4);
    }

    [Fact]
    public void Partial_delivery_has_no_completion_time()
    {
        var ledger = new FanoutLedger(1, 3);
        ledger.MarkSent(0, 100, 3);
        ledger.Record(0, 103);

        var summary = ledger.Summarize(Frequency);

        summary.DeliveryRatio.Should().BeApproximately(1 / 3.0, 1e-12);
        summary.MessagesComplete.Should().Be(0);
        summary.CompletionMs.Should().Be(LatencySummary.Empty);
        summary.LatencyMs.P50.Should().Be(3);
    }

    [Fact]
    public void Unknown_sequences_and_receives_beyond_capacity_are_strays_not_deliveries()
    {
        var ledger = new FanoutLedger(1, 1);
        ledger.MarkSent(0, 10, 1);
        ledger.Record(0, 11);
        ledger.Record(0, 12);
        ledger.Record(5, 12);
        ledger.Record(-1, 12);

        var summary = ledger.Summarize(Frequency);

        summary.Strays.Should().Be(3);
        summary.Receives.Should().Be(1);
        summary.DeliveryRatio.Should().Be(1.0);
        ledger.TotalReceived.Should().Be(1);
        ledger.TotalExpected.Should().Be(1);
    }

    [Fact]
    public void Messages_never_sent_are_left_out()
    {
        var ledger = new FanoutLedger(5, 2);
        ledger.MarkSent(0, 10, 2);
        ledger.Record(0, 11);
        ledger.Record(0, 11);

        var summary = ledger.Summarize(Frequency);
        summary.MessagesSent.Should().Be(1);
        summary.DeliveryRatio.Should().Be(1.0);
    }

    [Fact]
    public async Task Concurrent_receives_are_all_counted()
    {
        const int receivers = 2000;
        var ledger = new FanoutLedger(3, receivers);
        for (var m = 0; m < 3; m++)
        {
            ledger.MarkSent(m, 1, receivers);
        }

        await Parallel.ForAsync(0, receivers * 3, TestContext.Current.CancellationToken, (i, _) =>
        {
            ledger.Record(i % 3, 2 + (i % 7));
            return ValueTask.CompletedTask;
        });

        var summary = ledger.Summarize(Frequency);
        summary.Receives.Should().Be(receivers * 3);
        summary.MessagesComplete.Should().Be(3);
        summary.Strays.Should().Be(0);
        summary.LatencyMs.Max.Should().Be(7);
    }

    [Property]
    public Property Delivery_ratio_is_receives_over_expected_and_never_above_one()
    {
        var gen = Gen.Choose(1, 50).SelectMany(expected => Gen.Choose(0, 80).Select(received => (expected, received)));
        return Prop.ForAll(gen.ToArbitrary(), t =>
        {
            var ledger = new FanoutLedger(1, 100);
            ledger.MarkSent(0, 1, t.expected);
            for (var i = 0; i < t.received; i++)
            {
                ledger.Record(0, 2);
            }

            var summary = ledger.Summarize(Frequency);
            var expectedRatio = Math.Min(t.received, t.expected) / (double)t.expected;
            return Math.Abs(summary.DeliveryRatio - expectedRatio) < 1e-12
                && summary.DeliveryRatio <= 1.0
                && (summary.MessagesComplete == 1) == (t.received >= t.expected);
        });
    }

    [Fact]
    public void Constructor_rejects_nonsense()
    {
        var noMessages = () => new FanoutLedger(0, 1);
        var negative = () => new FanoutLedger(1, -1);
        noMessages.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
        var ledger = new FanoutLedger(1, 1);
        var badFrequency = () => ledger.Summarize(0);
        badFrequency.Should().Throw<ArgumentOutOfRangeException>();
    }
}
