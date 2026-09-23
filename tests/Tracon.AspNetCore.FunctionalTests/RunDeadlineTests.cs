using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ConcurrentToolInvocationTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <see cref="TraconAgentGraphOptions.MaxDuration"/> (Phase 128, T-7): the
/// run tree's fifth budget dimension. The cutoff always lands between two
/// model turns — a tool call already in progress when the deadline passes is
/// NOT interrupted — and applies on the queued (durable) run path too, where
/// no other wall-clock limit exists today.
/// </summary>
public sealed class RunDeadlineTests
{
    private const string AgentName = "deadline-agent";
    private const string ToolName = "slow_tool";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    private static AgentDefinition DeadlineAgent() => new()
    {
        Name = AgentName,
        Instructions = "Call the tool, then answer.",
        Model = new ModelBinding { Provider = "deadline-model", Model = "deadline-1" },
        ToolNames = [ToolName],
    };

    private static Uri RunUri(Guid runId) => new($"/tracon/api/runs/{runId}", UriKind.Relative);

    private static Uri CancelUri(Guid runId) => new($"/tracon/api/runs/{runId}/cancel", UriKind.Relative);

    private static Guid ExtractRunId(SseFrame runFrame)
    {
        var payload = System.Text.Json.JsonDocument.Parse(runFrame.Data).RootElement;
        return payload.GetProperty("runId").GetGuid();
    }

    [Fact]
    public async Task Deadline_cuts_the_run_off_between_two_model_turns_not_mid_tool()
    {
        // The tool advances the FAKE clock past the deadline itself, instead
        // of really sleeping: by the time it returns, the tree's time is
        // already up, and the cutoff must land on the NEXT model call
        // (the one that would produce "final answer"), never inside the tool.
        var clock = new ManualTimeProvider();

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("deadline-model")
                    .CallsTool(ToolName)
                    .RespondsWith("final answer"))
                .AddTool((Func<string>)(() =>
                {
                    clock.Advance(TimeSpan.FromSeconds(10));
                    return "tool result";
                }), name: ToolName)
                .AddAgent(DeadlineAgent()),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconOptions>(o => o.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5));
            });

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "do it" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        // The cutoff throws before the model produces "final answer" - that
        // text must never reach the wire, half-written or otherwise.
        frames.ShouldNotContain(static frame => frame.Data.Contains("final answer", StringComparison.Ordinal));
        frames[^1].Event.ShouldBe("error");

        var runId = ExtractRunId(frames[0]);
        var run = await host.Client.GetFromJsonAsync<RunRecord>(RunUri(runId));

        run.ShouldNotBeNull();
        run!.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Class.ShouldBe(RunErrorClass.QuotaExceeded);
    }

    [Fact]
    public async Task Deadline_is_enforced_on_the_queued_durable_run_path_too()
    {
        // 🚨 T-7's core claim: a queued run's lease is renewed continuously
        // (JobWorkerBackgroundService.StartLeaseRenewal) and today carries NO
        // wall-clock limit of its own. This proves MaxDuration cuts a queued
        // run off exactly like the synchronous path above.
        //
        // 🚨 No fake TimeProvider here, unlike the synchronous test above:
        // JobWorkerBackgroundService's own polling/lease-renewal loop reads
        // the SAME registered TimeProvider, and a clock that only advances
        // when the tool body calls it stalls that loop forever (measured: the
        // run never left Queued/Running). A short REAL MaxDuration plus a
        // REAL (short) tool delay exercises the same cutoff without freezing
        // the host's own background machinery.
        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("deadline-model")
                    .CallsTool(ToolName)
                    .RespondsWith("final answer"))
                .AddTool((Func<Task<string>>)(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500)); // delay: simulated
                    return "tool result";
                }), name: ToolName)
                .AddAgent(DeadlineAgent()),
            configureServices: services =>
            {
                services.Configure<TraconOptions>(o => o.AgentGraph.MaxDuration = TimeSpan.FromMilliseconds(200));
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(new AgentRunRequest { Message = "do it" }) };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Class.ShouldBe(RunErrorClass.QuotaExceeded);
    }

    [Fact]
    public async Task No_MaxDuration_set_leaves_todays_behavior_unchanged()
    {
        // Regression guard for the "Riskler" row: a zero (unset) MaxDuration
        // must not silently start cutting off runs that already run long.
        var clock = new ManualTimeProvider();

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("deadline-model")
                    .CallsTool(ToolName)
                    .RespondsWith("final answer"))
                .AddTool((Func<string>)(() =>
                {
                    clock.Advance(TimeSpan.FromDays(1));
                    return "tool result";
                }), name: ToolName)
                .AddAgent(DeadlineAgent()),
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "do it" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames.ShouldContain(static frame => frame.Data.Contains("final answer", StringComparison.Ordinal));

        var runId = ExtractRunId(frames[0]);
        var run = await host.Client.GetFromJsonAsync<RunRecord>(RunUri(runId));

        run!.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Cancelling_a_run_while_the_deadline_has_already_passed_still_classifies_as_Canceled()
    {
        // The cutoff (TraconRunBudgetExceededException -> QuotaExceeded)
        // only ever fires from a SYNCHRONOUS check made right before the next
        // model call. An explicit cancel that interrupts a tool call already
        // in flight must win: the run's terminal status is Canceled, never
        // QuotaExceeded, however close the deadline already is.
        var clock = new ManualTimeProvider();
        var toolStarted = new TaskCompletionSource();
        var releaseTool = new TaskCompletionSource();

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("deadline-model")
                    .CallsTool(ToolName)
                    .RespondsWith("final answer"))
                .AddTool(
                    AIFunctionFactory.Create(
                        async (CancellationToken cancellationToken) =>
                        {
                            toolStarted.TrySetResult();

                            // The deadline is already behind us by the time the
                            // tool returns - a slower cutoff check would have
                            // no chance to run before cancellation preempts it.
                            clock.Advance(TimeSpan.FromSeconds(10));

                            await releaseTool.Task.WaitAsync(cancellationToken);

                            return "tool result";
                        },
                        ToolName))
                .AddAgent(DeadlineAgent()),
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<TraconOptions>(o => o.AgentGraph.MaxDuration = TimeSpan.FromSeconds(5));
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(new AgentRunRequest { Message = "do it" }) };
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var frames = SseReader.ReadAsync(stream).GetAsyncEnumerator();

        (await frames.MoveNextAsync()).ShouldBeTrue();
        var runId = ExtractRunId(frames.Current);

        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using var cancelled = await host.Client.PostAsync(CancelUri(runId), content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        releaseTool.TrySetResult();

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Canceled);
    }
}
