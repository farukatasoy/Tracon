using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowInboxHarness;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A human request that runs out of time (phase 209): the run that waits on it is closed, an
/// answer to it is refused like an answer to any run that no longer waits, and what the run kept
/// for the answer is released to retention.
/// </summary>
/// <remarks>
/// <para>
/// The host reads a clock the test moves, and the expiry service looks every 50 ms, so a case
/// moves the clock past the limit and waits for the run to change. Every case drives the real
/// service over the real stores: the two writes it makes (the row, then the run) and the read it
/// makes in between are what a test of one part cannot see.
/// </para>
/// <para>
/// The graphs are registered in code, so their limit is the registration's; the stored-definition
/// cases save the workflow over HTTP and run it through the manager agent's plan approval.
/// </para>
/// </remarks>
public sealed class WorkflowRequestExpirationHttpTests
{
    private static readonly TimeSpan PastTheLimit = Limit + TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_run_whose_request_ran_out_of_time_is_closed_and_an_answer_to_it_is_refused()
    {
        var clock = new ManualTimeProvider();
        await using var host = await StartAsync(clock: clock);

        var (runId, requestId) = await RunToAwaitAsync(host, Timed);
        var runsBefore = (await WorkflowRunsAsync(host)).Count;

        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(runId, "Failed");

        var run = await RunAsync(host, runId);
        var error = run.GetProperty("error");

        error.GetProperty("type").GetString().ShouldBe("WorkflowRequestExpired");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain(requestId);
        error.GetProperty("class").GetString().ShouldBe("Timeout");
        run.GetProperty("eventCount").GetInt64().ShouldBeGreaterThan(0);

        // The run left AwaitingInput: the tenant's list and the run's own list no longer show it.
        (await ListAsync(host, "state=all")).ShouldBeEmpty();

        using var perRun = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(perRun)).GetArrayLength().ShouldBe(0);

        // An answer is refused as an answer to any run that is not waiting is, and opens no run.
        using var refused = await RespondAsync(host, runId, new { requestId, approved = true });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("AwaitingInput");
        (await WorkflowRunsAsync(host)).Count.ShouldBe(runsBefore);
    }

    [Fact]
    public async Task A_request_without_a_limit_never_expires()
    {
        var clock = new ManualTimeProvider();
        await using var host = await StartAsync(clock: clock);

        var (runId, _) = await RunToAwaitAsync(host, Open);

        clock.Advance(TimeSpan.FromDays(3650));
        await Task.Delay(400);

        (await RunAsync(host, runId)).GetProperty("status").GetString().ShouldBe("AwaitingInput");
        (await ListAsync(host)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_answered_request_closes_its_run_as_completed_and_not_as_a_failure()
    {
        var clock = new ManualTimeProvider();
        await using var host = await StartAsync(clock: clock);

        var (runId, requestId) = await RunToAwaitAsync(host, Timed);
        var (otherRun, _) = await RunToAwaitAsync(host, Timed);
        var responseRun = await AnswerAsync(host, runId, requestId);

        (await host.WaitForTerminalRunAsync(responseRun)).Status.ShouldBe(RunStatus.Completed);

        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(otherRun, "Failed");
        await host.WaitForRunStatusAsync(runId, "Completed");

        // The answer was given; the run it was given to ends without an error, and the run
        // nobody answered ends as the failure it is.
        var answered = await RunAsync(host, runId);

        answered.TryGetProperty("error", out var error).ShouldBeTrue();
        error.ValueKind.ShouldBe(JsonValueKind.Null);
        (await RunAsync(host, otherRun)).GetProperty("error").GetProperty("type").GetString().ShouldBe("WorkflowRequestExpired");
    }

    [Fact]
    public async Task Each_tenant_has_its_own_requests_closed()
    {
        var clock = new ManualTimeProvider();
        await using var host = await StartAsync(tenancy: true, clock: clock);

        var alpha = await RunToAwaitAsync(host, Timed, tenant: "alpha");
        var beta = await RunToAwaitAsync(host, Timed, tenant: "beta");

        await AnswerAsync(host, beta.RunId, beta.RequestId, tenant: "beta");

        clock.Advance(PastTheLimit);

        await host.WaitForRunStatusAsync(alpha.RunId, "Failed", tenant: "alpha");
        await host.WaitForRunStatusAsync(beta.RunId, "Completed", tenant: "beta");

        (await ListAsync(host, "state=all", tenant: "alpha")).ShouldBeEmpty();
        (await ListAsync(host, "state=all", tenant: "beta")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_workflow_expires_at_the_limit_of_the_version_the_run_recorded()
    {
        var clock = new ManualTimeProvider();
        await using var host = await StartAsync(
            clock: clock,
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition("manager"))
                .AddAgent(TestData.Definition("worker")));

        using (var saved = await SavePlannedAsync(host, "planned", Limit))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var first = await RunToAwaitAsync(host, "planned");

        // Saved again while the run waits: version 2 allows ten days. It changes nothing for the run
        // that waits, which keeps the limit of the version it recorded.
        using (var edited = await SavePlannedAsync(host, "planned", TimeSpan.FromDays(10)))
        {
            edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        }

        var second = await RunToAwaitAsync(host, "planned");

        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(first.RunId, "Failed");

        // The run started after the edit has ten days.
        (await RunAsync(host, second.RunId)).GetProperty("status").GetString().ShouldBe("AwaitingInput");
        (await ListAsync(host)).ShouldHaveSingleItem().GetProperty("runId").GetGuid().ShouldBe(second.RunId);
    }

    [Fact]
    public async Task What_a_waiting_run_keeps_is_kept_until_its_request_expires_and_released_after()
    {
        using var database = new TempSqliteDatabase("request-expiry-retention");
        var clock = new ManualTimeProvider();
        await using var host = await StartAsync(
            clock: clock,
            configureTracon: builder => builder.UseSqlite(database.ConnectionString));

        var (runId, _) = await RunToAwaitAsync(host, Timed);
        var retention = host.Services.GetRequiredService<IRetentionStore>();
        var checkpoints = host.Services.GetRequiredService<IWorkflowCheckpointStore>();
        var runs = host.Services.GetRequiredService<IRunStore>();
        var everythingIsOld = DateTimeOffset.UtcNow.AddDays(1);
        const string Tenant = "default";

        (await checkpoints.ListByRunAsync(Tenant, runId)).ShouldNotBeEmpty();

        // Waiting: a retention round that finds everything old still leaves the request and the
        // checkpoints, because answering needs them (K-888).
        await retention.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, everythingIsOld, batchSize: 1000);
        await retention.DeleteBatchAsync(RetentionTargets.WorkflowCheckpoints, tenantId: null, everythingIsOld, batchSize: 1000);

        (await runs.ReadEventsAsync(runId, 0).ToListAsync()).ShouldContain(static runEvent => runEvent.Type == RunEventType.WorkflowRequest);
        (await checkpoints.ListByRunAsync(Tenant, runId)).ShouldNotBeEmpty();

        // The request expires, the run is closed, and the next round releases what it kept.
        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(runId, "Failed");

        await retention.DeleteBatchAsync(RetentionTargets.RunEvents, tenantId: null, everythingIsOld, batchSize: 1000);
        await retention.DeleteBatchAsync(RetentionTargets.WorkflowCheckpoints, tenantId: null, everythingIsOld, batchSize: 1000);

        (await runs.ReadEventsAsync(runId, 0).ToListAsync()).ShouldNotContain(static runEvent => runEvent.Type == RunEventType.WorkflowRequest);
        (await checkpoints.ListByRunAsync(Tenant, runId)).ShouldBeEmpty();

        // The closed row goes by age like the rest.
        var inbox = host.Services.GetRequiredService<IWorkflowRequestStore>();

        (await inbox.ListByRunAsync(runId)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Closed);
        (await retention.DeleteBatchAsync(RetentionTargets.WorkflowRequests, tenantId: null, DateTimeOffset.UtcNow.AddDays(30), batchSize: 1000))
            .ShouldBe(1);
        (await inbox.ListByRunAsync(runId)).ShouldBeEmpty();
    }
}
