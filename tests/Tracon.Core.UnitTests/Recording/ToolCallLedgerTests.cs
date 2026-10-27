using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The tool-call ledger (F-689): what it keeps of a call, in what order it hands the calls back, and that it hands
/// each one over once.
/// </summary>
public sealed class ToolCallLedgerTests
{
    private static FunctionCallContent Call(string callId, string name = "lookup")
        => new(callId, name, arguments: new Dictionary<string, object?>(StringComparer.Ordinal) { ["city"] = "Ankara" });

    [Fact]
    public void A_settled_call_keeps_its_result_its_times_and_how_long_it_took()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);

        var started = clock.GetUtcNow();
        var entry = ledger.Begin(Call("call-1")).ShouldNotBeNull();

        clock.Advance(TimeSpan.FromMilliseconds(250));
        ledger.Settle(entry, "sunny");

        var snapshot = ledger.Unclaimed().ShouldHaveSingleItem();

        snapshot.Call.Name.ShouldBe("lookup");
        snapshot.Call.CallId.ShouldBe("call-1");
        snapshot.IsSettled.ShouldBeTrue();
        snapshot.Result.ShouldBe("sunny");
        snapshot.Failure.ShouldBeNull();
        snapshot.StartedAtUtc.ShouldBe(started);
        snapshot.EndedAtUtc.ShouldBe(started + TimeSpan.FromMilliseconds(250));
        snapshot.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void A_call_that_ended_with_an_exception_keeps_the_exception_and_no_result()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);
        var failure = new InvalidOperationException("upstream connection reset");

        var entry = ledger.Begin(Call("call-1")).ShouldNotBeNull();
        clock.Advance(TimeSpan.FromSeconds(1));
        ledger.Fail(entry, failure);

        var snapshot = ledger.Unclaimed().ShouldHaveSingleItem();

        snapshot.IsSettled.ShouldBeTrue();
        snapshot.Failure.ShouldBeSameAs(failure);
        snapshot.Result.ShouldBeNull();
        snapshot.Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_call_that_has_not_ended_is_open_and_has_no_duration()
    {
        var ledger = new ToolCallLedger(new ManualTimeProvider());

        ledger.Begin(Call("call-1")).ShouldNotBeNull();

        var snapshot = ledger.Unclaimed().ShouldHaveSingleItem();

        snapshot.IsSettled.ShouldBeFalse();
        snapshot.EndedAtUtc.ShouldBeNull();
        snapshot.Duration.ShouldBeNull();
    }

    [Fact]
    public void A_call_that_is_already_open_belongs_to_the_layer_that_began_it()
    {
        // The tool chain composed twice around one tool: the inner layer must not record the call again.
        var ledger = new ToolCallLedger(new ManualTimeProvider());

        ledger.Begin(Call("call-1")).ShouldNotBeNull();

        ledger.Begin(Call("call-1")).ShouldBeNull();
        ledger.Unclaimed().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_identity_used_again_after_its_call_ended_is_a_new_call()
    {
        // A provider that repeats an identity across two turns: each call is its own row.
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);

        ledger.Settle(ledger.Begin(Call("call-1")).ShouldNotBeNull(), "first");
        clock.Advance(TimeSpan.FromSeconds(1));
        ledger.Settle(ledger.Begin(Call("call-1")).ShouldNotBeNull(), "second");

        ledger.Unclaimed().Select(static call => call.Result).ShouldBe(["first", "second"]);
    }

    [Fact]
    public void A_call_settled_twice_keeps_its_first_outcome()
    {
        var ledger = new ToolCallLedger(new ManualTimeProvider());
        var entry = ledger.Begin(Call("call-1")).ShouldNotBeNull();

        ledger.Settle(entry, "first");
        ledger.Fail(entry, new InvalidOperationException("late"));

        var snapshot = ledger.Unclaimed().ShouldHaveSingleItem();

        snapshot.Result.ShouldBe("first");
        snapshot.Failure.ShouldBeNull();
    }

    [Fact]
    public void Unclaimed_lists_the_calls_in_the_order_they_started_and_claims_nothing()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);

        ledger.Begin(Call("call-a", "first"));
        clock.Advance(TimeSpan.FromMilliseconds(1));
        ledger.Begin(Call("call-b", "second"));

        ledger.Unclaimed().Select(static call => call.Call.Name).ShouldBe(["first", "second"]);
        ledger.Unclaimed().Count.ShouldBe(2, "reading the ledger must not use a call up");
    }

    [Fact]
    public void A_claimed_call_is_handed_over_once_and_leaves_the_unclaimed_list()
    {
        var ledger = new ToolCallLedger(new ManualTimeProvider());
        ledger.Settle(ledger.Begin(Call("call-1")).ShouldNotBeNull(), "sunny");

        ledger.Claim("call-1").ShouldNotBeNull().Result.ShouldBe("sunny");

        ledger.Claim("call-1").ShouldBeNull();
        ledger.Unclaimed().ShouldBeEmpty();
    }

    [Fact]
    public void A_claim_takes_the_oldest_call_with_the_identity_first()
    {
        var clock = new ManualTimeProvider();
        var ledger = new ToolCallLedger(clock);

        ledger.Settle(ledger.Begin(Call("call-1")).ShouldNotBeNull(), "first");
        clock.Advance(TimeSpan.FromSeconds(1));
        ledger.Settle(ledger.Begin(Call("call-1")).ShouldNotBeNull(), "second");

        ledger.Claim("call-1").ShouldNotBeNull().Result.ShouldBe("first");
        ledger.Claim("call-1").ShouldNotBeNull().Result.ShouldBe("second");
        ledger.Claim("call-1").ShouldBeNull();
    }

    [Fact]
    public void A_call_that_did_not_pass_through_the_chain_is_not_in_the_ledger()
    {
        // A hosted tool, or a tool of the framework: the message is the only record of such a call.
        var ledger = new ToolCallLedger(new ManualTimeProvider());

        ledger.Claim("hosted-call").ShouldBeNull();
    }

    [Fact]
    public void A_claimed_call_that_never_ended_does_not_hold_back_a_new_call_with_its_identity()
    {
        var ledger = new ToolCallLedger(new ManualTimeProvider());

        ledger.Begin(Call("call-1")).ShouldNotBeNull();
        ledger.Claim("call-1").ShouldNotBeNull();

        ledger.Begin(Call("call-1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Calls_that_run_at_the_same_time_are_all_kept()
    {
        // AllowConcurrentToolCalls: the chain writes from several threads at once.
        var ledger = new ToolCallLedger(TimeProvider.System);

        await Parallel.ForAsync(
            0,
            200,
            TestContext.Current.CancellationToken,
            (index, _) =>
            {
                var entry = ledger.Begin(Call($"call-{index}")).ShouldNotBeNull();
                ledger.Settle(entry, index);

                return ValueTask.CompletedTask;
            });

        var calls = ledger.Unclaimed();

        calls.Count.ShouldBe(200);
        calls.ShouldAllBe(static call => call.IsSettled);
        calls.Select(static call => call.Call.CallId).Distinct(StringComparer.Ordinal).Count().ShouldBe(200);
    }
}
