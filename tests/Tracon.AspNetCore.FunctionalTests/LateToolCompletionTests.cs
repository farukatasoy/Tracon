using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ApprovalEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost share a
// name, so a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool call that outlives its timeout is cancelled, and when a body that
/// ignores that cancellation succeeds anyway its result and its reported spend
/// are written onto the call's own record.
/// </summary>
/// <remarks>
/// <para>
/// Driven through a REAL Microsoft Agent Framework tool-calling loop rather
/// than by constructing the wrappers directly, for K-218's reason: an isolated
/// construction proves nothing about the pipeline. The call identity these
/// records hang off comes from <c>FunctionInvokingChatClient</c>, which only
/// exists on the real path.
/// </para>
/// <para>
/// 🚨 What this closes: the run's record said <c>timedOut: true, usage: null,
/// result: null</c> while the provider had produced and billed a real image.
/// The measurement the tool reported did reach the run's accumulator; nothing
/// ever took it back out, so the charge left no trace in any cost report.
/// </para>
/// </remarks>
public sealed class LateToolCompletionTests
{
    private static readonly Uri Run = new("/tracon/api/agents/late-agent/run", UriKind.Relative);

    private static async Task<Guid> StartAsync(TraconTestHost host, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "make the report", SessionId = sessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<ToolInvocationRecord> ReadSingleInvocationAsync(TraconTestHost host, Guid runId)
    {
        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        return invocations.ShouldNotBeNull().ShouldHaveSingleItem();
    }

    /// <summary>
    /// Polls the call record until the late write lands. The background body
    /// settles on its own schedule; a fixed sleep would be a flake either way.
    /// </summary>
    private static Task<ToolInvocationRecord> WaitForLateSettlementAsync(TraconTestHost host, Guid runId)
        => WaitUntil.ValueAsync(
            () => ReadSingleInvocationAsync(host, runId),
            static record => record.LateCompletedAt is not null,
            $"the call record of run {runId} to record a late settlement");

    /// <summary>
    /// A body that finishes only when the test says so, and ignores the
    /// cancellation the timeout requests.
    /// </summary>
    /// <remarks>
    /// Phase 184: these bodies used to sleep 600 ms - 1 s against the 300 ms
    /// timeout, so each test relied on one timer firing before another. The
    /// test now releases the body after the run has completed, which is by
    /// construction after the timeout fired.
    /// </remarks>
    private static TaskCompletionSource NewRelease() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TraconToolRegistration SlowTool(Func<CancellationToken, Task<string>> body)
        => new(
            Microsoft.Extensions.AI.AIFunctionFactory.Create(body, "slow_report"),
            timeout: TimeSpan.FromMilliseconds(300));

    private static Action<ITraconBuilder> Builder(TraconToolRegistration registration)
        => builder =>
        {
            builder
                .AddModelProvider(new FakeModelProvider("late-model")
                    .CallsTool("slow_report", new { reportId = "R-1" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = "late-agent",
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "late-model", Model = "late-1" },
                    ToolNames = ["slow_report"],
                });

            builder.Services.AddSingleton(registration);
        };

    [Fact]
    public async Task A_tool_that_succeeds_after_its_timeout_has_its_spend_recorded_against_the_original_call()
    {
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = NewRelease();

        await using var host = await TraconTestHost.StartAsync(
            Builder(SlowTool(async _ =>
            {
                // Deliberately ignores the cancellation the timeout requests —
                // the non-cooperative body that is the whole reason this path
                // exists. A real one is an HTTP call that does not take a token.
                await release.Task;

                TraconToolUsage.Report(new ToolCallUsage
                {
                    Unit = ToolUsageUnits.Images,
                    Quantity = 1m,
                    Cost = 0.04m,
                    Currency = "USD",
                });

                finished.TrySetResult(true);

                return "report R-1 produced";
            })),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        var runId = await StartAsync(host, "s-late-success");

        // The model is told the call failed and the run finishes on time.
        await host.WaitForRunStatusAsync(runId, "Completed");

        var timedOut = await ReadSingleInvocationAsync(host, runId);
        timedOut.TimedOut.ShouldBeTrue();

        release.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var settled = await WaitForLateSettlementAsync(host, runId);

        // What the model was told is history and is NOT rewritten.
        settled.TimedOut.ShouldBeTrue();
        settled.Succeeded.ShouldBeFalse();
        settled.Error.ShouldNotBeNullOrEmpty();

        // What the timeout left empty is now filled in — this is the charge
        // that used to vanish.
        settled.Usage.ShouldNotBeNull();
        settled.Usage.Cost.ShouldBe(0.04m);
        settled.Usage.Quantity.ShouldBe(1m);
        string.Equals(settled.Usage.Unit, ToolUsageUnits.Images, StringComparison.Ordinal).ShouldBeTrue();
        settled.Result.ShouldNotBeNull();
        settled.Result.ShouldContain("report R-1 produced");
        settled.Duration.ShouldNotBeNull();
        settled.Duration.Value.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task A_late_settlement_is_still_one_call_in_the_per_tool_totals()
    {
        // 🚨 The reason the outcome lands on the EXISTING row rather than a new
        // one: the tools screen counts rows. A second row would report one call
        // as two and halve the tool's error rate.
        var release = NewRelease();

        await using var host = await TraconTestHost.StartAsync(
            Builder(SlowTool(async _ =>
            {
                await release.Task;

                return "report R-1 produced";
            })),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        var runId = await StartAsync(host, "s-late-count");

        await host.WaitForRunStatusAsync(runId, "Completed");
        release.SetResult();
        await WaitForLateSettlementAsync(host, runId);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var usage = (await runs.GetToolUsageAsync(new ToolUsageQuery()))
            .Single(tool => string.Equals(tool.ToolName, "slow_report", StringComparison.Ordinal));

        usage.TotalCalls.ShouldBe(1);
        usage.FailedCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_cooperative_tool_is_stopped_by_the_timeout_and_books_nothing()
    {
        // The other half of the repair, and the more valuable one: a body that
        // reads its token spends nothing after the timeout, so there is nothing
        // to account for. 🚨 Measured on the old code: it was never cancelled
        // and ran to completion.
        var outcome = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await TraconTestHost.StartAsync(
            Builder(SlowTool(async cancellationToken =>
            {
                try
                {
                    // Finite only so a body that is never cancelled fails with its
                    // own words below instead of a bare timeout.
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // delay: simulated
                    outcome.TrySetResult("ran to completion");

                    return "report R-1 produced";
                }
                catch (OperationCanceledException)
                {
                    outcome.TrySetResult("cancelled");

                    throw;
                }
            })),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        var runId = await StartAsync(host, "s-late-cooperative");

        await host.WaitForRunStatusAsync(runId, "Completed");

        (await outcome.Task.WaitAsync(TimeSpan.FromSeconds(20))).ShouldBe("cancelled");

        var record = await ReadSingleInvocationAsync(host, runId);

        record.TimedOut.ShouldBeTrue();

        // A cancelled body produced nothing, so nothing is claimed for it.
        record.LateCompletedAt.ShouldBeNull();
        record.Usage.ShouldBeNull();
        record.Result.ShouldBeNull();
    }

    [Fact]
    public async Task A_tool_that_faults_after_its_timeout_books_nothing()
    {
        // A late FAILURE costs nothing and changes no accounting: the model was
        // told the call failed, and it did.
        var faulted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = NewRelease();

        await using var host = await TraconTestHost.StartAsync(
            Builder(SlowTool(async _ =>
            {
                await release.Task;
                faulted.TrySetResult(true);

                throw new InvalidOperationException("the provider rejected it");
            })),
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        var runId = await StartAsync(host, "s-late-fault");

        await host.WaitForRunStatusAsync(runId, "Completed");
        release.SetResult();
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Phase 184: a fixed one-second "fair window" stood here, and under load
        // the observer might not have run inside it - a green test that proved
        // nothing. The observer logs its decision before it returns, and a
        // faulted body returns without writing, so once the line is there no
        // write can still follow.
        await WaitUntil.TrueAsync(
            () => host.Logs.AllText.Contains("Tool 'slow_report' faulted after", StringComparison.Ordinal),
            "the late-settlement observer to see the fault");

        var record = await ReadSingleInvocationAsync(host, runId);

        record.TimedOut.ShouldBeTrue();
        record.LateCompletedAt.ShouldBeNull();
        record.Usage.ShouldBeNull();
    }
}
