using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Same rationale as ToolGovernanceEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <see cref="ModelBinding.AllowConcurrentToolCalls"/> (Phase 81, F-134):
/// three independent tool calls issued in the SAME turn, driven through a
/// REAL <c>FunctionInvokingChatClient</c> loop (via <see cref="FakeModelProvider"/>,
/// same rationale as ToolGovernanceEndpointTests.cs), not by invoking
/// <c>AIFunction</c> bodies directly.
/// </summary>
/// <remarks>
/// 🚨 A <see cref="Barrier"/> forces genuine overlap: none of the three tool
/// bodies can return before ALL THREE have entered, so the concurrent path is
/// GUARANTEED to actually run at the same time, not "probably" (a
/// <c>Task.Delay</c>-based approximation would be test theater).
/// </remarks>
public sealed class ConcurrentToolInvocationTests
{
    private const string AgentName = "concurrent-agent";

    private static async Task<HttpResponseMessage> PostQueuedAsync(TraconTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request);
    }

    /// <summary>Polls a run until it reaches the expected status. See ApprovalEndpointTests.cs for the 30s rationale.</summary>
    private static async Task<string> WaitForStatusAsync(TraconTestHost host, Guid runId, string expected)
    {
        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var poll = await host.Client.GetAsync(uri);
            status = (await TraconTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                return status!;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            $"Run {runId} did not reach status '{expected}' within 30 seconds; last seen status: '{status}'.");
    }

    private static AgentDefinition ConcurrentAgent(bool allowConcurrentToolCalls) => new()
    {
        Name = AgentName,
        Instructions = "Give a short answer.",
        Model = new ModelBinding
        {
            Provider = "concurrent-model",
            Model = "concurrent-1",
            AllowConcurrentToolCalls = allowConcurrentToolCalls,
        },
        ToolNames = ["tool_a", "tool_b", "tool_c"],
    };

    private sealed class DenyingHandler(string deniedToolName) : IToolAuthorizationHandler
    {
        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(string.Equals(request.ToolName, deniedToolName, StringComparison.Ordinal)
                ? ToolAuthorizationResult.Deny("Not authorized.")
                : ToolAuthorizationResult.Allow());
    }

    [Fact]
    public async Task Three_concurrent_tool_calls_are_all_recorded_correctly_under_a_guaranteed_overlap()
    {
        using var barrier = new Barrier(3);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("concurrent-model")
                    .CallsTools(("tool_a", null), ("tool_b", null), ("tool_c", null))
                    .RespondsWith("all done"))
                .AddTool((Func<Task<string>>)(() => Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    TraconToolUsage.Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 1 });
                    return "result_a";
                })), name: "tool_a")
                .AddTool((Func<Task<string>>)(() => Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    TraconToolUsage.Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 2 });
                    return "result_b";
                })), name: "tool_b")
                .AddTool((Func<Task<string>>)(() => Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                    TraconToolUsage.Report(new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = 3 });
                    return "result_c";
                })), name: "tool_c")
                .AddAgent(ConcurrentAgent(allowConcurrentToolCalls: true)),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "run all three", SessionId = "s-concurrent-1" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        // If the barrier were never satisfied (the calls did NOT genuinely
        // overlap), this would time out instead of completing.
        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        invocations.ShouldNotBeNull();
        invocations!.Count.ShouldBe(3);

        // 🚨 MEAI wraps every AIFunctionFactory (delegate) result in a JsonElement
        // (measured: Microsoft.Extensions.AI.OpenAIChatClient.ToOpenAIChatMessages
        // uses `Result as string` verbatim, but falls back to
        // `JsonSerializer.Serialize(Result, ...)` for anything else, and a JsonElement's
        // own converter round-trips its raw text) - so a delegate tool returning a bare
        // `string` reaches the model JSON-quoted, unlike a source-generated tool
        // returning `string` directly. `ToolInvocationRecord.Result` mirrors the wire
        // bytes exactly (Phase 102 / K-615), so the expectation here carries the quotes.
        foreach (var (name, result, quantity) in new[] { ("tool_a", "\"result_a\"", 1m), ("tool_b", "\"result_b\"", 2m), ("tool_c", "\"result_c\"", 3m) })
        {
            var record = invocations.Where(item => string.Equals(item.ToolName, name, StringComparison.Ordinal)).ShouldHaveSingleItem();
            record.Result.ShouldBe(result);
            record.Succeeded.ShouldBeTrue();
            record.AuthorizationDenied.ShouldBeFalse();

            // 🚨 Regression (audit finding, phase 81 closure): TraconToolUsage.Report
            // reads the call identity from the FunctionInvokingChatClient.CurrentContext
            // AsyncLocal; under real concurrent dispatch a wrongly-shared ambient
            // context would attribute every quantity to the SAME (usually last) call.
            record.Usage.ShouldNotBeNull();
            record.Usage!.Quantity.ShouldBe(quantity);
        }
    }

    [Fact]
    public async Task Authorization_denial_under_concurrency_is_bound_to_the_correct_call()
    {
        // Barrier(2), not 3: tool_b is DENIED before its body ever runs, so
        // only tool_a and tool_c actually reach the barrier.
        using var barrier = new Barrier(2);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("concurrent-model")
                    .CallsTools(("tool_a", null), ("tool_b", null), ("tool_c", null))
                    .RespondsWith("all done"))
                .AddTool((Func<Task<string>>)(() => Task.Run(() => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return "result_a"; })), name: "tool_a")
                .AddTool((Func<Task<string>>)(() => Task.Run(() => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return "result_b"; })), name: "tool_b")
                .AddTool((Func<Task<string>>)(() => Task.Run(() => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return "result_c"; })), name: "tool_c")
                .AddAgent(ConcurrentAgent(allowConcurrentToolCalls: true)),
            configureServices: static services =>
            {
                services.AddSingleton<IToolAuthorizationHandler>(new DenyingHandler("tool_b"));
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "run all three", SessionId = "s-concurrent-2" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        invocations.ShouldNotBeNull();

        // 🚨 The regression this guards: ToolAuthorizationAccumulator keyed by
        // the WRONG call identity under real concurrent access would bind the
        // denial to a sibling call instead - or to none of them.
        invocations!.Where(static item => string.Equals(item.ToolName, "tool_a", StringComparison.Ordinal)).ShouldHaveSingleItem()
            .AuthorizationDenied.ShouldBeFalse();
        invocations.Where(static item => string.Equals(item.ToolName, "tool_b", StringComparison.Ordinal)).ShouldHaveSingleItem()
            .AuthorizationDenied.ShouldBeTrue();
        invocations.Where(static item => string.Equals(item.ToolName, "tool_c", StringComparison.Ordinal)).ShouldHaveSingleItem()
            .AuthorizationDenied.ShouldBeFalse();
    }

    [Fact]
    public async Task One_tool_timing_out_does_not_stop_its_siblings_from_completing()
    {
        // 🚨 Regression (audit finding, phase 81 closure): tool_b's own
        // timeout must bound only ITS call. Under real concurrent dispatch, a
        // wrongly-shared cancellation would also cut off tool_a/tool_c, which
        // have no timeout of their own and are only slower than tool_b's short one.
        using var barrier = new Barrier(3);

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider("concurrent-model")
                        .CallsTools(("tool_a", null), ("tool_b", null), ("tool_c", null))
                        .RespondsWith("all done"))
                    .AddTool((Func<Task<string>>)(() => Task.Run(() => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return "result_a"; })), name: "tool_a")
                    .AddTool((Func<Task<string>>)(() => Task.Run(() => { barrier.SignalAndWait(TimeSpan.FromSeconds(10)); return "result_c"; })), name: "tool_c")
                    .AddAgent(ConcurrentAgent(allowConcurrentToolCalls: true));

                // tool_b's body waits at the SAME barrier as its siblings, then
                // sleeps well past its own 200ms timeout - it must time out
                // WITHOUT affecting tool_a/tool_c, which carry no timeout at all.
                builder.Services.AddSingleton(new TraconToolRegistration(
                    Microsoft.Extensions.AI.AIFunctionFactory.Create(
                        () => Task.Run(async () =>
                        {
                            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            return "result_b";
                        }),
                        "tool_b"),
                    timeout: TimeSpan.FromMilliseconds(200)));

                builder.Services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "run all three", SessionId = "s-concurrent-timeout" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        invocations.ShouldNotBeNull();

        invocations!.Where(static item => string.Equals(item.ToolName, "tool_a", StringComparison.Ordinal)).ShouldHaveSingleItem()
            .Succeeded.ShouldBeTrue();
        invocations.Where(static item => string.Equals(item.ToolName, "tool_c", StringComparison.Ordinal)).ShouldHaveSingleItem()
            .Succeeded.ShouldBeTrue();

        var timedOut = invocations.Where(static item => string.Equals(item.ToolName, "tool_b", StringComparison.Ordinal)).ShouldHaveSingleItem();
        timedOut.TimedOut.ShouldBeTrue();
        timedOut.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Flag_off_preserves_todays_sequential_guarantee()
    {
        using var mutex = new SemaphoreSlim(1, 1);
        var overlapDetected = false;

        string RunExclusively(string result)
        {
            if (!mutex.Wait(0))
            {
                // Another tool body is still inside: sequential execution was violated.
                overlapDetected = true;
                return result;
            }

            try
            {
                Thread.Sleep(20);
                return result;
            }
            finally
            {
                mutex.Release();
            }
        }

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("concurrent-model")
                    .CallsTools(("tool_a", null), ("tool_b", null), ("tool_c", null))
                    .RespondsWith("all done"))
                .AddTool((Func<string>)(() => RunExclusively("result_a")), name: "tool_a")
                .AddTool((Func<string>)(() => RunExclusively("result_b")), name: "tool_b")
                .AddTool((Func<string>)(() => RunExclusively("result_c")), name: "tool_c")
                .AddAgent(ConcurrentAgent(allowConcurrentToolCalls: false)),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "run all three", SessionId = "s-sequential" });
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "Completed")).ShouldBe("Completed");

        overlapDetected.ShouldBeFalse();

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        invocations.ShouldNotBeNull();
        invocations!.Count.ShouldBe(3);
    }
}
