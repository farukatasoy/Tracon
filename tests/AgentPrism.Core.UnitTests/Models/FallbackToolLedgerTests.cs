using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Verifies the tool ledger a fallback chain shares across its links (phase
/// 124, K-1): a tool call completed on one link must not run a second time
/// when the model asks the same question again on the next link.
/// </summary>
/// <remarks>
/// The ledger mechanics (<see cref="RecordedToolPlayback"/> with
/// <c>recordLiveCalls: true</c>) are exercised directly for the cases that
/// need no chat-client machinery; the chain-level cases construct
/// <see cref="FallbackChatClient"/> directly (not through
/// <see cref="ModelProviderRegistry"/> — <see cref="FallbackChatClientTests"/>
/// already proves the wrapping POSITION) with fakes that call
/// <see cref="ChatOptions.Tools"/> the same way <c>FunctionInvokingChatClient</c>
/// would.
/// </remarks>
public sealed class FallbackToolLedgerTests
{
    private static readonly ChatMessage[] Messages = [new ChatMessage(ChatRole.User, "hi")];

    // ---- Ledger mechanics (RecordedToolPlayback directly) ----

    [Fact]
    public async Task A_live_call_is_recorded_and_the_next_identical_call_is_answered_from_the_ledger()
    {
        var realCalls = 0;
        var firstLink = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var secondLink = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 41 };

        var ledger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive, recordLiveCalls: true);

        var first = await ledger.Wrap(firstLink).InvokeAsync(args, TestContext.Current.CancellationToken);
        var second = await ledger.Wrap(secondLink).InvokeAsync(args, TestContext.Current.CancellationToken);

        // The ledger replays the recorded TEXT, not the original CLR value
        // (AIFunctionFactory serializes a delegate's return value) — same
        // rule as RecordedToolPlaybackTests, only the text matters here.
        second.ShouldNotBeNull().ToString().ShouldBe(first!.ToString());
        realCalls.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_wrapper_asking_twice_never_answers_itself_from_the_ledger()
    {
        // Regression (found by independent audit): the SAME wrapper is what
        // one link's own internal tool-call loop reuses across its OWN
        // turns — no fallback link is involved here at all. Answering a
        // repeat from the ledger in that case would be indistinguishable
        // from a genuine cross-link replay, but it is a DIFFERENT event: the
        // model legitimately asking the same question twice within one
        // successful link (rolling dice twice, generating two random
        // values, ...) must run the body twice, exactly as if no ledger
        // existed.
        var realCalls = 0;
        var function = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 41 };

        var ledger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive, recordLiveCalls: true);
        var wrapped = ledger.Wrap(function);

        await wrapped.InvokeAsync(args, TestContext.Current.CancellationToken);
        await wrapped.InvokeAsync(args, TestContext.Current.CancellationToken);

        realCalls.ShouldBe(2);
    }

    [Fact]
    public async Task A_zero_argument_call_is_recorded_and_answered_from_the_ledger_across_wrappers()
    {
        var realCalls = 0;
        var firstLink = AIFunctionFactory.Create(() => { realCalls++; return "rolled"; }, "roll_dice");
        var secondLink = AIFunctionFactory.Create(() => { realCalls++; return "rolled"; }, "roll_dice");

        var ledger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive, recordLiveCalls: true);
        var noArguments = new AIFunctionArguments(StringComparer.Ordinal);

        await ledger.Wrap(firstLink).InvokeAsync(noArguments, TestContext.Current.CancellationToken);
        await ledger.Wrap(secondLink).InvokeAsync(noArguments, TestContext.Current.CancellationToken);

        realCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_call_with_different_arguments_is_not_blocked_by_an_unrelated_recorded_call()
    {
        var realCalls = 0;
        var function = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var ledger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive, recordLiveCalls: true);

        await ledger.Wrap(function).InvokeAsync(
            new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 1 }, TestContext.Current.CancellationToken);
        await ledger.Wrap(function).InvokeAsync(
            new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 2 }, TestContext.Current.CancellationToken);

        realCalls.ShouldBe(2);
    }

    [Fact]
    public async Task A_live_call_that_threw_is_recorded_and_replayed_as_a_failure_not_a_success()
    {
        var realCalls = 0;
        AIFunction Explode() => AIFunctionFactory.Create(
            (int x) =>
            {
                realCalls++;
                throw new InvalidOperationException("boom");
            },
            "explode");

        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 1 };
        var ledger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive, recordLiveCalls: true);

        await Should.ThrowAsync<InvalidOperationException>(
            () => ledger.Wrap(Explode()).InvokeAsync(args, TestContext.Current.CancellationToken).AsTask());

        var exception = await Should.ThrowAsync<AgentPrismException>(
            () => ledger.Wrap(Explode()).InvokeAsync(args, TestContext.Current.CancellationToken).AsTask());

        // The raw exception message never reaches the replayed text unredacted
        // (ToolFailureText.Get) — only the generic, type-named form does.
        exception.Message.ShouldContain("InvalidOperationException");
        exception.Message.ShouldNotContain("boom");
        realCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RecordLiveCalls_false_keeps_continuations_existing_behavior_every_call_runs_live()
    {
        var realCalls = 0;
        var function = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 41 };

        // Default recordLiveCalls: false — RunContinuationJobHandler's own usage.
        var ledger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive);

        await ledger.Wrap(function).InvokeAsync(args, TestContext.Current.CancellationToken);
        await ledger.Wrap(function).InvokeAsync(args, TestContext.Current.CancellationToken);

        realCalls.ShouldBe(2);
    }

    // ---- Chain-level (FallbackChatClient directly) ----

    [Fact]
    public async Task Fallback_reuses_a_tool_call_completed_by_the_primary_but_still_runs_a_new_one()
    {
        var repeatedCalls = 0;
        var newCalls = 0;
        var repeated = AIFunctionFactory.Create((int x) => { repeatedCalls++; return x + 1; }, "increment");
        var fresh = AIFunctionFactory.Create((int x) => { newCalls++; return x * 2; }, "double");

        var repeatedArgs = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 41 };
        var freshArgs = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 10 };

        // Primary: calls "increment", then fails as if the outage hit right after.
        var primary = new ToolCallingChatClient([("increment", repeatedArgs)], failAfterCalls: true);

        // Fallback: asks for "increment" again (must be answered from the
        // ledger) AND calls "double" for the first time (must run for real).
        var fallback = new ToolCallingChatClient([("increment", repeatedArgs), ("double", freshArgs)], failAfterCalls: false);

        var client = new FallbackChatClient(
            Binding("primary", "fallback"),
            primary,
            (_, _) => new ValueTask<IChatClient>(fallback));

        var options = new ChatOptions { Tools = [repeated, fresh] };
        var response = await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        response.Text.ShouldBe("20");
        repeatedCalls.ShouldBe(1);
        newCalls.ShouldBe(1);

        // The caller's own options object is untouched: its Tools still
        // carry the ORIGINAL, unwrapped functions.
        options.Tools!.ShouldContain(repeated);
        options.Tools!.ShouldContain(fresh);
    }

    [Fact]
    public async Task A_primary_that_never_falls_back_still_runs_a_repeated_identical_call_twice()
    {
        // Regression (found by independent audit): the primary link (index 0)
        // is wrapped with the ledger too — necessarily, so its results are
        // available if a LATER link needs them. But when the primary simply
        // calls the same tool with the same arguments twice and then
        // SUCCEEDS (no fallback ever triggers), the second call must not be
        // silently answered from what the first one just wrote.
        var realCalls = 0;
        var tool = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 41 };

        var primary = new ToolCallingChatClient([("increment", args), ("increment", args)], failAfterCalls: false);

        var client = new FallbackChatClient(
            Binding("primary", "fallback"),
            primary,
            (_, _) => throw new InvalidOperationException("The fallback must never be resolved."));

        var options = new ChatOptions { Tools = [tool] };
        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        realCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Third_link_answers_from_the_ledger_written_by_the_second_link_without_rerunning_the_tool()
    {
        var realCalls = 0;
        var tool = AIFunctionFactory.Create((int x) => { realCalls++; return x + 1; }, "increment");
        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 41 };

        // Primary never even reaches a tool call.
        var primary = new ThrowingChatClient(new AgentPrismProviderUnavailableException("circuit open"));

        // First fallback link actually runs the tool, then fails.
        var fallback1 = new ToolCallingChatClient([("increment", args)], failAfterCalls: true);

        // Second fallback link asks the same question; must be answered from
        // what the FIRST fallback link recorded, not run again.
        var fallback2 = new ToolCallingChatClient([("increment", args)], failAfterCalls: false);

        var clients = new Dictionary<string, IChatClient>(StringComparer.Ordinal)
        {
            ["fallback1"] = fallback1,
            ["fallback2"] = fallback2,
        };

        var binding = new ModelBinding
        {
            Provider = "primary",
            Model = "primary-model",
            Fallbacks =
            [
                new ModelFallback { Provider = "fallback1", Model = "fallback1-model" },
                new ModelFallback { Provider = "fallback2", Model = "fallback2-model" },
            ],
        };

        var client = new FallbackChatClient(binding, primary, (b, _) => new ValueTask<IChatClient>(clients[b.Provider]));

        var options = new ChatOptions { Tools = [tool] };
        var response = await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        response.Text.ShouldBe("42");
        realCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_cancellation_thrown_by_the_tool_body_propagates_and_the_fallback_is_never_resolved()
    {
        static int Explode(int x) => throw new OperationCanceledException();
        var tool = AIFunctionFactory.Create((Func<int, int>)Explode, "increment");
        var args = new AIFunctionArguments(StringComparer.Ordinal) { ["x"] = 1 };

        var primary = new ToolCallingChatClient([("increment", args)], failAfterCalls: false, propagateToolExceptions: true);

        var client = new FallbackChatClient(
            Binding("primary", "fallback"),
            primary,
            (_, _) => throw new InvalidOperationException("The fallback must never be resolved."));

        var options = new ChatOptions { Tools = [tool] };

        await Should.ThrowAsync<OperationCanceledException>(
            () => client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Streaming_path_does_not_wrap_tools_with_the_ledger()
    {
        var tool = AIFunctionFactory.Create((int x) => x + 1, "increment");

        var primary = new ThrowingBeforeFirstChunkStreamClient(new AgentPrismProviderUnavailableException("circuit open"));
        AITool? seenByFallback = null;
        var fallback = new StreamingToolCapturingClient(options => seenByFallback = options?.Tools?.SingleOrDefault());

        var client = new FallbackChatClient(
            Binding("primary", "fallback"),
            primary,
            (_, _) => new ValueTask<IChatClient>(fallback));

        var options = new ChatOptions { Tools = [tool] };

        await foreach (var _ in client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken))
        {
        }

        // No PlaybackFunction indirection: the fallback link sees the SAME
        // AIFunction instance the caller passed in.
        ReferenceEquals(seenByFallback, tool).ShouldBeTrue();
    }

    private static ModelBinding Binding(string primary, string fallback)
        => new()
        {
            Provider = primary,
            Model = "primary-model",
            Fallbacks = [new ModelFallback { Provider = fallback, Model = "fallback-model" }],
        };

    /// <summary>
    /// A fake link that calls named tools (from <c>options.Tools</c>) in
    /// order, mirroring how <c>FunctionInvokingChatClient</c> drives a tool —
    /// including that a tool's own exception becomes part of the
    /// conversation rather than propagating to the caller of
    /// <see cref="GetResponseAsync"/>, unless
    /// <paramref name="propagateToolExceptions"/> says otherwise (needed for
    /// the cancellation case, which a real loop never swallows either).
    /// </summary>
    private sealed class ToolCallingChatClient(
        IReadOnlyList<(string Name, AIFunctionArguments Arguments)> calls,
        bool failAfterCalls,
        bool propagateToolExceptions = false) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var lastText = "no-calls";

            foreach (var (name, arguments) in calls)
            {
                var tool = (AIFunction)options!.Tools!.Single(
                    candidate => string.Equals(((AIFunction)candidate).Name, name, StringComparison.Ordinal));

                if (propagateToolExceptions)
                {
                    var result = await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
                    lastText = result?.ToString() ?? "null";
                    continue;
                }

                try
                {
                    var result = await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
                    lastText = result?.ToString() ?? "null";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastText = $"error:{ex.Message}";
                }
            }

            if (failAfterCalls)
            {
                throw new InvalidOperationException("HTTP 500 (mid-turn)");
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, lastText));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingBeforeFirstChunkStreamClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162 // Unreachable code: satisfies the iterator's yield-bearing signature.
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StreamingToolCapturingClient(Action<ChatOptions?> onCalled) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            onCalled(options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "fallback chunk");
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
