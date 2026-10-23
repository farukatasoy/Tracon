using System.Net;
using AGUI.Abstractions;
using Tracon.AGUI.FunctionalTests.Infrastructure;

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
