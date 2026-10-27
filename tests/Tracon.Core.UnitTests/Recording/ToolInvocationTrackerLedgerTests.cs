using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// What <see cref="ToolInvocationTracker"/> takes from the tool-call ledger (F-689): the duration and the settle time
/// of a run that does not stream, and the claim that keeps a call to one row.
/// </summary>
public sealed class ToolInvocationTrackerLedgerTests
{
    private static FunctionCallContent Call(string callId) => new(callId, "lookup", arguments: null);

    [Fact]
    public void A_run_that_does_not_stream_takes_the_duration_and_the_settle_time_from_the_ledger()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: false, clock, ledger: ledger);

        var call = Call("call-1");
        var entry = ledger.Begin(call).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromMilliseconds(250));
        ledger.Settle(entry, "sunny");
        var settledAt = clock.GetUtcNow();

        // The run recorder reads the messages a minute later.
        clock.Advance(TimeSpan.FromMinutes(1));
        tracker.OnCall(call, source: null, arguments: null);

        var record = tracker.OnResult(new FunctionResultContent("call-1", "sunny"));

        record.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
        record.CreatedAt.ShouldBe(settledAt, "the row carries the moment the call settled, not the moment it was read");
    }

    [Fact]
    public void A_run_that_streams_keeps_its_own_measurement()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: true, clock, ledger: ledger);

        var call = Call("call-1");
        ledger.Settle(ledger.Begin(call).ShouldNotBeNull(), "sunny");

        clock.Advance(TimeSpan.FromSeconds(10));
        tracker.OnCall(call, source: null, arguments: null);
        clock.Advance(TimeSpan.FromSeconds(2));

        var record = tracker.OnResult(new FunctionResultContent("call-1", "sunny"));

        record.Duration.ShouldBe(TimeSpan.FromSeconds(2), "the streaming path measures between the two contents, as before");
        record.CreatedAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void A_call_outside_the_tool_chain_has_no_duration_in_a_run_that_does_not_stream()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: false, clock, ledger: new ToolCallLedger(clock));

        tracker.OnCall(Call("hosted-1"), source: null, arguments: null);

        var record = tracker.OnResult(new FunctionResultContent("hosted-1", "result"));

        record.Duration.ShouldBeNull();
        record.CreatedAt.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void Writing_the_row_claims_the_call_so_the_close_step_finds_nothing_left()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: false, clock, ledger: ledger);

        var call = Call("call-1");
        ledger.Settle(ledger.Begin(call).ShouldNotBeNull(), "sunny");
        tracker.OnCall(call, source: null, arguments: null);

        tracker.OnResult(new FunctionResultContent("call-1", "sunny"));

        ledger.Unclaimed().ShouldBeEmpty();
    }

    [Fact]
    public void A_call_the_tracker_holds_as_started_is_reported_as_pending_until_it_is_written()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: false, clock);

        tracker.IsPending("call-1").ShouldBeFalse();

        tracker.OnCall(Call("call-1"), source: null, arguments: null);
        tracker.IsPending("call-1").ShouldBeTrue();

        tracker.OnResult(new FunctionResultContent("call-1", "sunny"));
        tracker.IsPending("call-1").ShouldBeFalse();
    }

    [Fact]
    public void An_unfinished_call_is_closed_with_the_time_it_ran_and_claimed()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: false, clock, ledger: ledger);

        var call = Call("call-1");
        ledger.Begin(call).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromSeconds(3));

        // The run is cancelled while the call runs; the close step registers the call, then drains it.
        tracker.OnCall(call, source: null, arguments: null);
        var record = tracker.DrainUnfinished("The run ended before the tool result arrived.").ShouldHaveSingleItem();

        record.Succeeded.ShouldBeFalse();
        record.Error.ShouldBe("The run ended before the tool result arrived.");
        record.Duration.ShouldBe(TimeSpan.FromSeconds(3));
        ledger.Unclaimed().ShouldBeEmpty();
    }

    [Fact]
    public void An_unfinished_call_has_no_duration_without_a_ledger_in_a_run_that_does_not_stream()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ToolInvocationTracker(TraconId.NewId(), measureDuration: false, clock);

        tracker.OnCall(Call("call-1"), source: null, arguments: null);

        tracker.DrainUnfinished("The run ended before the tool result arrived.").ShouldHaveSingleItem().Duration.ShouldBeNull();
    }
}
