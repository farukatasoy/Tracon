using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The streams that follow a run by polling its store wait between two polls on the clock the host registered, for the
/// interval <see cref="TraconEndpointOptions.RunEventPollInterval"/> names.
/// </summary>
/// <remarks>
/// <para>
/// Each poll that finds nothing new writes a <c>: waiting</c> comment, so the comment is the tick of the poll. A host on a
/// <see cref="ManualClock"/> that is never moved writes no second comment; moving it by the interval writes exactly one
/// more. No test waits for the interval to pass.
/// </para>
/// <para>
/// The interval is seven seconds: neither a default nor the keep-alive interval of the stream writer, so a timer of that
/// length in <see cref="ManualClock.Pending"/> is the poll's.
/// </para>
/// </remarks>
public sealed class RunEventPollClockTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(7);

    [Fact]
    public async Task A_run_event_stream_polls_the_store_again_when_the_hosts_clock_moves_by_the_poll_interval()
    {
        var clock = new ManualClock();
        var gated = new GatedModelProvider("gated", "gated-1");
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(gated)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = gated.Name, Model = gated.Models[0].Name } }),
            configureServices: services => UseClock(services, clock));

        try
        {
            // The run's own request stays open: the model holds it. Nobody reads it.
            using var runRequest = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/kod-agent/run")
            {
                Content = JsonContent.Create(new { message = "stream me" }),
            };
            using var runResponse = await host.Client.SendAsync(runRequest, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

            await gated.Started;

            var runs = host.Services.GetRequiredService<IRunStore>();
            var recorded = await WaitUntil.ValueAsync(
                async () => await runs.QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken),
                static found => found.Count > 0,
                "the run to be recorded");

            using var events = await host.Client.GetAsync(
                new Uri($"/tracon/api/runs/{recorded[0].Id}/events", UriKind.Relative),
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken);
            using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

            // The first look found the run's own events and wrote them; the stream waits one interval before it looks
            // again, and the second look finds nothing new.
            await WaitUntil.TrueAsync(() => clock.Pending.Contains(PollInterval), "the poll to wait one interval on the host's clock");

            clock.Advance(PollInterval);

            await SseComments.ReadUntilLineAsync(reader, SseComments.Waiting);
        }
        finally
        {
            // The model ignores cancellation: a case that failed before this point would otherwise hold the host open.
            gated.Open();
        }
    }

    [Fact]
    public async Task A_follower_of_an_interrupted_answer_looks_for_the_continuation_again_when_the_hosts_clock_moves_by_the_poll_interval()
    {
        var clock = new ManualClock();
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false, configureServices: services => UseClock(services, clock));
        var (sourceId, requestId, head) = await InterruptHeadAsync(host);

        using var follower = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: TestContext.Current.CancellationToken);
        using var reader = new StreamReader(await follower.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        // The head announces itself; the job still owns the work, so the follower waits for its continuation.
        var announced = await SseComments.ReadUntilLineAsync(reader, SseComments.Waiting);

        announced.ShouldContain(line => line.Contains(head.ToString(), StringComparison.Ordinal));
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(PollInterval), "the follower to wait one interval on the host's clock");

        var continuation = await StartContinuationAsync(host, sourceId, head);

        clock.Advance(PollInterval);

        await SseComments.ReadUntilAsync(
            reader,
            line => line.StartsWith("data: ", StringComparison.Ordinal) && line.Contains(continuation.ToString(), StringComparison.Ordinal),
            "the frame that announces the continuation");
    }

    [Fact]
    public async Task A_follower_of_a_running_continuation_polls_its_events_again_when_the_hosts_clock_moves_by_the_poll_interval()
    {
        var clock = new ManualClock();
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false, configureServices: services => UseClock(services, clock));
        var (sourceId, requestId, head) = await InterruptHeadAsync(host);

        await StartContinuationAsync(host, sourceId, head);

        using var follower = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: TestContext.Current.CancellationToken);
        using var reader = new StreamReader(await follower.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        await SseComments.ReadUntilLineAsync(reader, SseComments.Waiting);
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(PollInterval), "the poll to wait one interval on the host's clock");

        clock.Advance(PollInterval);

        await SseComments.ReadUntilLineAsync(reader, SseComments.Waiting);
    }

    private static void UseClock(IServiceCollection services, ManualClock clock)
    {
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(new TestEndpointOptionsSetup(static options => options.RunEventPollInterval = PollInterval));
    }

    /// <summary>
    /// Answers a waiting request through the queue and fails the run that took the answer as an interrupted process does,
    /// so that the job still owns the work and no continuation exists yet.
    /// </summary>
    private static async Task<(Guid SourceId, string RequestId, Guid Head)> InterruptHeadAsync(TraconTestHost host)
    {
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(
            host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var head = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);

        await host.Services.GetRequiredService<IRunStore>().CompleteRunAsync(new RunCompletion
        {
            RunId = head,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "interrupted", Message = "The process stopped." },
            TenantId = QueuedWorkflowHarness.TenantOf(host),
        });

        return (sourceId, requestId, head);
    }

    /// <summary>Opens the run that continues the interrupted head, as the job's recovery does.</summary>
    private static async Task<Guid> StartContinuationAsync(TraconTestHost host, Guid sourceId, Guid head)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var continuation = Guid.NewGuid();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = continuation,
            AgentName = SingleFlow,
            Kind = RunKind.Workflow,
            WorkflowName = SingleFlow,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = QueuedWorkflowHarness.TenantOf(host),
            SessionId = (await runs.GetRunAsync(sourceId))!.SessionId,
            ContinuedFromRunId = head,
        });

        return continuation;
    }
}
