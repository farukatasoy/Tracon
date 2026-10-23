using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// What a front end is told when a run fails after the stream began (Phase 236).
/// </summary>
public sealed class AGUIFailureTests
{
    private const string InternalAddress = "http://10.0.0.5:9999/v1/chat";

    [Fact]
    public async Task A_failure_after_the_stream_began_is_a_RUN_ERROR_that_tells_the_cause_but_not_the_address()
    {
        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(new FailingModelProvider("failing-model", $"connect to {InternalAddress} refused"))
            .AddAgent(TestAgents.Definition("failing-agent", "failing-model")));

        var response = await host.RunAsync("failing-agent", AGUIInputs.User("t-fail", "hello"));

        // The status line was sent before the failure, so the failure is an event, never a silent end.
        response.Status.ShouldBe(HttpStatusCode.OK);
        response.Types[0].ShouldBe("RUN_STARTED");

        var error = response.Events.OfType<RunErrorEvent>().ShouldHaveSingleItem();
        error.Code.ShouldBe("ProviderInvocationException", "Tracon normalizes a provider SDK failure into its own exception type");
        error.Message.ShouldContain("failing-model", Case.Sensitive);
        response.Types.ShouldNotContain("RUN_FINISHED", StringComparer.Ordinal, "a failed run does not also finish");

        // 🚨 A provider exception names the address it could not reach; that stays in the server's log.
        response.Body.ShouldNotContain("10.0.0.5", Case.Sensitive);
        host.Logs.Lines.ShouldContain(line => line.StartsWith("Error", StringComparison.Ordinal) && line.Contains("(ref: ", StringComparison.Ordinal));

        var record = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { SessionId = "t-fail" }, TestContext.Current.CancellationToken);
        record.ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task A_run_store_that_cannot_take_the_ignored_tools_notice_does_not_fail_the_run()
    {
        // Observability does not break functionality: the notice is written after the stream, best effort.
        await using var host = await AGUITestHost.StartAsync(builder =>
        {
            TestAgents.AddEcho(builder);
            builder.Services.ThrowFrom<IRunStore>(
                "AppendEventAsync",
                static () => new InvalidOperationException("run store unavailable"),
                when: static arguments => arguments is [RunEvent { CustomType: RunEventCustomTypes.AGUIToolsIgnored }, ..]);
        });

        var response = await host.RunAsync(
            TestAgents.Echo,
            AGUIInputs.User("t-notice", "hi", "r1", AGUIInputs.Tool("not_carried")));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Types[^1].ShouldBe("RUN_FINISHED");
        response.Events.OfType<RunErrorEvent>().ShouldBeEmpty();
        response.Text.ShouldBe("Echo: hi");
        host.Logs.Lines.ShouldContain(line => line.StartsWith("Warning", StringComparison.Ordinal) && line.Contains("could not be recorded", StringComparison.Ordinal));

        var record = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(response.TraconRunId, TestContext.Current.CancellationToken);
        record!.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task The_ignored_tools_notice_is_bounded_in_count_and_in_name_length()
    {
        // The names come from a request body, so the notice that records them has a ceiling: 50 names of 128 characters.
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        var tools = Enumerable.Range(0, 60)
            .Select(index => AGUIInputs.Tool($"t{index:D2}_" + new string('x', 200)))
            .ToArray();

        var response = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-bound", "hi", "r1", tools));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);

        string? payload = null;

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(response.TraconRunId, 0, TestContext.Current.CancellationToken))
        {
            if (string.Equals(runEvent.CustomType, RunEventCustomTypes.AGUIToolsIgnored, StringComparison.Ordinal))
            {
                payload = runEvent.Payload;
            }
        }

        var names = JsonDocument.Parse(payload!).RootElement.GetProperty("tools").EnumerateArray().Select(static name => name.GetString()!).ToList();

        names.Count.ShouldBe(50);
        names.ShouldAllBe(static name => name.Length <= 128);
    }

    [Fact]
    public async Task A_session_store_that_cannot_be_read_applies_no_answer_and_the_same_answer_works_once_it_recovers()
    {
        // The answer is matched against the session's history, which the store holds. A store that cannot be read must not
        // turn into "nothing pending" and must not apply the decision: the failure is the request's, and a retry succeeds.
        var executions = new StrongBox<int>();
        var failing = new StrongBox<bool>();

        await using var host = await AGUITestHost.StartAsync(builder =>
        {
            TestAgents.AddApprovalTool(builder, executions);
            builder.Services.ThrowFrom<ISessionStore>(
                "GetAsync",
                static () => new InvalidOperationException("session store unavailable"),
                when: _ => failing.Value);
        });

        var first = await host.RunAsync(TestAgents.Approval, AGUIInputs.User("t-store", "cancel the order", "r1"));
        var interruptId = first.Interrupts.ShouldHaveSingleItem().Id!;

        failing.Value = true;

        await Should.ThrowAsync<InvalidOperationException>(() => host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.With("t-store", "r2", [], resume: [AGUIInputs.Resolved(interruptId, new { approved = true })])));

        executions.Value.ShouldBe(0);

        failing.Value = false;

        var retried = await host.RunAsync(
            TestAgents.Approval,
            AGUIInputs.With("t-store", "r3", [], resume: [AGUIInputs.Resolved(interruptId, new { approved = true })]));

        retried.Status.ShouldBe(HttpStatusCode.OK, retried.Body);
        executions.Value.ShouldBe(1);
    }

    [Fact]
    public async Task Two_runs_of_one_thread_that_overlap_leave_the_loser_a_RUN_ERROR_and_a_recorded_conflict()
    {
        // The session rule is the management API's: the run that saves second finds the session changed under it.
        var gated = new GatedModelProvider("gated-model");
        await using var host = await AGUITestHost.StartAsync(builder => builder
            .AddModelProvider(gated)
            .AddAgent(TestAgents.Definition("gated-agent", "gated-model")));

        // Both runs open the thread before either saves it.
        var first = host.RunAsync("gated-agent", AGUIInputs.User("t-race", "first", "r1"));
        await gated.FirstCallHeld.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var second = await host.RunAsync("gated-agent", AGUIInputs.User("t-race", "second", "r2"));
        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        second.Types[^1].ShouldBe("RUN_FINISHED");

        gated.ReleaseFirstCall();
        var loser = await first;

        loser.Events.OfType<RunErrorEvent>().ShouldHaveSingleItem().Code.ShouldBe(nameof(TraconSessionConflictException));

        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { SessionId = "t-race" }, TestContext.Current.CancellationToken);
        runs.Count.ShouldBe(2);

        var conflicted = 0;

        foreach (var run in runs)
        {
            await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(run.Id, 0, TestContext.Current.CancellationToken))
            {
                if (runEvent.Type == RunEventType.SessionWriteConflicted)
                {
                    conflicted++;
                }
            }
        }

        conflicted.ShouldBe(1, "the losing run records that its session write lost the race");
    }
}
