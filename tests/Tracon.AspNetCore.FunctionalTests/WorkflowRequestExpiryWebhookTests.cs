using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowInboxHarness;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that the request expiry service fails publishes <c>run.failed</c> (phase 211, F-539): the receiver of the
/// webhooks was told the run waits and now hears that it ended.
/// </summary>
/// <remarks>
/// <para>
/// The service closed the run in the store and published nothing - a known edge of phase 209 - so a host that drives its
/// pipeline from webhooks waited for a run that had already ended. The payload is the runner's own, from one builder.
/// </para>
/// <para>
/// An answered request's run is closed by the same service as <c>Completed</c> and publishes nothing: the answer's own run
/// published its <c>run.completed</c> when it ended, and a second one a year later would count one production twice.
/// </para>
/// </remarks>
public sealed class WorkflowRequestExpiryWebhookTests
{
    private static readonly TimeSpan PastTheLimit = Limit + TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_run_failed_by_an_expired_request_publishes_run_failed_with_the_runs_payload()
    {
        var clock = new ManualTimeProvider();
        var publisher = new RecordingWebhookPublisher();

        await using var host = await StartAsync(clock: clock, configureServices: services => services.AddSingleton<IWebhookPublisher>(publisher));

        var (runId, requestId) = await RunToAwaitAsync(host, Timed);

        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(runId, "Failed");
        await WaitUntil.TrueAsync(() => publisher.Runs(WebhookEvents.RunFailed).Count == 1, "run.failed to be published");

        var failed = publisher.Runs(WebhookEvents.RunFailed).ShouldHaveSingleItem();

        failed.RunId.ShouldBe(runId.ToString());
        failed.RootRunId.ShouldBe(runId.ToString());
        failed.AgentName.ShouldBe(Timed);
        failed.Kind.ShouldBe(nameof(RunKind.Workflow));
        failed.Status.ShouldBe(nameof(RunStatus.Failed));
        failed.Error.ShouldNotBeNull().ShouldContain(requestId, Case.Sensitive);
        failed.DurationMs.GetValueOrDefault().ShouldBeGreaterThanOrEqualTo((long)Limit.TotalMilliseconds);

        // The first event of the run, sent when it began to wait, is still the only other thing it published.
        publisher.Events.Select(static entry => entry.EventType)
            .ShouldBe([WebhookEvents.WorkflowRequestPending, WebhookEvents.RunFailed]);
        publisher.Events[^1].TenantId.ShouldBe((await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!.TenantId);
    }

    [Fact]
    public async Task An_answered_requests_run_closes_without_a_second_run_completed()
    {
        var clock = new ManualTimeProvider();
        var publisher = new RecordingWebhookPublisher();

        await using var host = await StartAsync(clock: clock, configureServices: services => services.AddSingleton<IWebhookPublisher>(publisher));

        var (runId, requestId) = await RunToAwaitAsync(host, Timed);
        var responseRun = await AnswerAsync(host, runId, requestId);

        (await host.WaitForTerminalRunAsync(responseRun)).Status.ShouldBe(RunStatus.Completed);

        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(runId, "Completed");

        // Give the service the scans it needs to have published, if it were going to.
        await Task.Delay(300);

        // The answer's run published its own completion; the source run, closed at the request's time, published nothing.
        publisher.Runs(WebhookEvents.RunCompleted).ShouldHaveSingleItem().RunId.ShouldBe(responseRun.ToString());
        publisher.Runs(WebhookEvents.RunFailed).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_publisher_that_fails_does_not_undo_the_close_or_stop_the_next_one()
    {
        var clock = new ManualTimeProvider();
        var publisher = new RecordingWebhookPublisher();

        await using var host = await StartAsync(clock: clock, configureServices: services => services.AddSingleton<IWebhookPublisher>(publisher));

        var (first, _) = await RunToAwaitAsync(host, Timed);
        var (second, _) = await RunToAwaitAsync(host, Timed);

        publisher.Throws = true;
        clock.Advance(PastTheLimit);

        // Both runs are closed although every publish throws, and the failure is in the log, not in the run.
        await host.WaitForRunStatusAsync(first, "Failed");
        await host.WaitForRunStatusAsync(second, "Failed");
        await WaitUntil.TrueAsync(
            () => host.Logs.AllText.Contains("closed for an expired request, could not be published", StringComparison.Ordinal),
            "the failed publish to be logged");

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(first))!.Error!.Type.ShouldBe("WorkflowRequestExpired");
        publisher.Runs(WebhookEvents.RunFailed).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_host_without_a_publisher_closes_the_run_as_before()
    {
        var clock = new ManualTimeProvider();

        await using var host = await StartAsync(clock: clock);

        var (runId, _) = await RunToAwaitAsync(host, Timed);

        clock.Advance(PastTheLimit);
        await host.WaitForRunStatusAsync(runId, "Failed");
    }
}
