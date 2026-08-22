using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Replay;

/// <summary>
/// <see cref="RecordedToolPlayback"/>'s mismatch-policy contract (Phase 87.3):
/// replay's <see cref="ToolPlaybackMismatchPolicy.Stop"/> behavior must stay
/// unchanged, and continuation's <see cref="ToolPlaybackMismatchPolicy.RunLive"/>
/// must run the real tool body for a call with no recorded result — this is
/// the phase's first (and most likely) failure mode.
/// </summary>
public sealed class RecordedToolPlaybackTests
{
    [Fact]
    public async Task Matched_call_returns_the_recorded_result_under_either_policy()
    {
        var record = Record("weather", "city=Paris", result: "sunny");
        var liveCalls = 0;
        var function = CountingFunction("weather", () => liveCalls++);

        foreach (var policy in new[] { ToolPlaybackMismatchPolicy.Stop, ToolPlaybackMismatchPolicy.RunLive })
        {
            liveCalls = 0;
            var playback = new RecordedToolPlayback([record], policy);
            var wrapped = playback.Wrap(function);

            var result = await wrapped.InvokeAsync(Arguments("city", "Paris"), TestContext.Current.CancellationToken);

            result.ShouldBe("sunny");
            liveCalls.ShouldBe(0);
            playback.Mismatch.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Stop_policy_records_the_mismatch_and_never_runs_the_real_body()
    {
        var liveCalls = 0;
        var function = CountingFunction("weather", () => liveCalls++);
        var playback = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.Stop);
        var wrapped = playback.Wrap(function);

        var result = await wrapped.InvokeAsync(Arguments("city", "Paris"), TestContext.Current.CancellationToken);

        liveCalls.ShouldBe(0);
        playback.Mismatch.ShouldNotBeNull();
        Should.Throw<ReplayToolMismatchException>(() => playback.ThrowIfMismatched());

        // The marker string, not the tool's real result — the loop was cut,
        // this text is only ever visible in the stream.
        result.ShouldBeOfType<string>().ShouldContain("Replay stopped");
    }

    [Fact]
    public async Task RunLive_policy_runs_the_real_body_on_a_mismatch_and_records_no_mismatch()
    {
        var liveCalls = 0;
        var function = CountingFunction("weather", () => liveCalls++, liveResult: "live-sunny");

        // Empty ledger: every call is, by definition, past the interruption point.
        var playback = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive);
        var wrapped = playback.Wrap(function);

        var result = await wrapped.InvokeAsync(Arguments("city", "Paris"), TestContext.Current.CancellationToken);

        liveCalls.ShouldBe(1);

        // The real tool body ran; AIFunctionFactory serializes a delegate's
        // return value, so the live result is not the SAME string instance
        // (or even the same CLR type) as the recorded one -- only its text matters here.
        result.ShouldNotBeNull().ToString()!.ShouldContain("live-sunny");
        playback.Mismatch.ShouldBeNull();
        Should.NotThrow(() => playback.ThrowIfMismatched());
    }

    [Fact]
    public async Task RunLive_policy_is_a_hybrid_earlier_calls_replay_later_calls_run_live()
    {
        // Only the FIRST of two identical calls was recorded -- the second
        // happened after the interruption point.
        var record = Record("weather", "city=Paris", result: "recorded-sunny");
        var liveCalls = 0;
        var function = CountingFunction("weather", () => liveCalls++, liveResult: "live-sunny");
        var playback = new RecordedToolPlayback([record], ToolPlaybackMismatchPolicy.RunLive);
        var wrapped = playback.Wrap(function);

        var first = await wrapped.InvokeAsync(Arguments("city", "Paris"), TestContext.Current.CancellationToken);
        var second = await wrapped.InvokeAsync(Arguments("city", "Paris"), TestContext.Current.CancellationToken);

        first.ShouldBe("recorded-sunny");
        second.ShouldNotBeNull().ToString()!.ShouldContain("live-sunny");
        liveCalls.ShouldBe(1);
        playback.Mismatch.ShouldBeNull();
    }

    [Fact]
    public async Task RunLive_policy_still_replays_the_recorded_error_for_a_matched_failed_call()
    {
        var record = Record("weather", "city=Paris", result: null, error: "provider unavailable");
        var liveCalls = 0;
        var function = CountingFunction("weather", () => liveCalls++);
        var playback = new RecordedToolPlayback([record], ToolPlaybackMismatchPolicy.RunLive);
        var wrapped = playback.Wrap(function);

        await Should.ThrowAsync<AgentPrismException>(
            () => wrapped.InvokeAsync(Arguments("city", "Paris"), TestContext.Current.CancellationToken).AsTask());

        liveCalls.ShouldBe(0);
    }

    private static AIFunctionArguments Arguments(string key, string value)
        => new(StringComparer.Ordinal) { [key] = value };

    private static ToolInvocationRecord Record(string toolName, string? arguments, string? result, string? error = null)
        => new()
        {
            Id = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            ToolName = toolName,
            Arguments = arguments,
            Result = result,
            Error = error,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static AIFunction CountingFunction(string name, Action onLiveCall, string liveResult = "live-result")
        => AIFunctionFactory.Create(
            (string city) =>
            {
                onLiveCall();
                return liveResult;
            },
            name);
}
