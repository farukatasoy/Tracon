using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every documented webhook event is published by the code path that owns it.
/// </summary>
/// <remarks>
/// F-333: <c>workflow.request.pending</c>, <c>job.completed</c>,
/// <c>job.failed</c> and <c>eval.completed</c> were defined, accepted on
/// subscribe and promised on the site, but nothing published them; and a
/// workflow root, whose agent steps are child runs, produced no <c>run.*</c>
/// event at all. A subscriber got silence and no error. The cases run through
/// the real host — DI, the real worker, the real workflow runner — with the
/// publisher seam replaced, because the defect was a missing publish call
/// and not the delivery.
/// </remarks>
public sealed class WebhookEventPublishingTests
{
    [Fact]
    public async Task A_job_that_fails_publishes_job_failed()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkerAsync(publisher);
        var jobId = await TriggerBatchAsync(host, "no-such-agent");

        var job = (await publisher.WaitForAsync(WebhookEvents.JobFailed)).Job.ShouldNotBeNull();

        job.JobId.ShouldBe(jobId.ToString());
        job.Kind.ShouldBe(JobHandlerKeys.AgentBatch);
        job.Status.ShouldBe(nameof(JobStatus.Failed));
        job.TotalItems.ShouldBe(1);
        job.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_job_that_completes_publishes_job_completed()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkerAsync(publisher);
        var jobId = await TriggerBatchAsync(host, TestData.Definition().Name);

        var job = (await publisher.WaitForAsync(WebhookEvents.JobCompleted)).Job.ShouldNotBeNull();

        job.JobId.ShouldBe(jobId.ToString());
        job.DoneItems.ShouldBe(1);
        publisher.Events.ShouldNotContain(entry => string.Equals(entry.EventType, WebhookEvents.JobFailed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_webhook_delivery_job_publishes_no_job_event()
    {
        // Its own job.* event would queue another delivery job for it, without end.
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkerAsync(publisher);

        var jobs = host.Services.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;

        // A delivery id with no record: the handler throws and the single
        // attempt fails the job, which is the status a job.failed would follow.
        var job = await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                HandlerKey = JobHandlerKeys.WebhookDelivery,
                TargetName = "missing-subscription",
                Status = JobStatus.Pending,
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [Guid.NewGuid().ToString()]);

        await WaitUntil.TrueAsync(
            async () => (await jobs.GetAsync("default", job.Id))?.Status is JobStatus.Completed or JobStatus.Failed,
            "the delivery job to finish");

        publisher.Events.ShouldNotContain(entry => entry.EventType.StartsWith("job.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_workflow_run_publishes_run_completed_as_a_workflow()
    {
        var publisher = new RecordingPublisher();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition("writer"))
                .UseWorkflows()
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "pass",
                    _ => (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)),
            configureServices: services => services.AddSingleton<IWebhookPublisher>(publisher));

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/chain",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Sequential,
                       Nodes =
                       [
                           new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                           new WorkflowNodeReference { Name = "pass", Kind = WorkflowNodeKind.Function },
                       ],
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        await RunToEndAsync(host, "/tracon/api/workflows/chain/run");

        var root = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(static run => run.Kind == RunKind.Workflow);

        var run = (await publisher.WaitForAsync(WebhookEvents.RunCompleted, entry => string.Equals(entry.Run?.Kind, nameof(RunKind.Workflow), StringComparison.Ordinal)))
            .Run.ShouldNotBeNull();

        run.RunId.ShouldBe(root.Id.ToString());
        run.AgentName.ShouldBe("chain");
        run.Status.ShouldBe(nameof(RunStatus.Completed));
    }

    [Fact]
    public async Task A_workflow_waiting_for_input_publishes_workflow_request_pending()
    {
        var publisher = new RecordingPublisher();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .UseWorkflows()
                .AddWorkflow("approval-flow", static _ => BuildApprovalWorkflow(), "Waits for a yes or no."),
            configureServices: services => services.AddSingleton<IWebhookPublisher>(publisher));

        await RunToEndAsync(host, "/tracon/api/workflows/approval-flow/run");

        var root = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.AwaitingInput);

        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{root.Id}/requests", UriKind.Relative));
        var requestId = (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString();

        var pending = (await publisher.WaitForAsync(WebhookEvents.WorkflowRequestPending)).Approval.ShouldNotBeNull();

        pending.RequestId.ShouldBe(requestId);
        pending.RunId.ShouldBe(root.Id.ToString());
        pending.WorkflowName.ShouldBe("approval-flow");

        // A summary only: the prompt stays behind GET .../requests.
        pending.Prompt.ShouldBeNull();
        publisher.Events.ShouldNotContain(entry => entry.EventType.StartsWith("run.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_eval_run_publishes_eval_completed_with_its_counts()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartWorkerAsync(publisher);

        using (var suite = await host.Client.PutAsJsonAsync(
                   "/tracon/api/evals/webhook-suite",
                   new EvalSuiteSaveRequest
                   {
                       AgentName = TestData.Definition().Name,
                       Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
                   }))
        {
            suite.StatusCode.ShouldBe(HttpStatusCode.OK, await suite.Content.ReadAsStringAsync());
        }

        using (var cases = await host.Client.PutAsJsonAsync(
                   "/tracon/api/evals/webhook-suite/cases",
                   new object[] { new { query = "first" }, new { query = "second" } }))
        {
            cases.StatusCode.ShouldBe(HttpStatusCode.OK, await cases.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/evals/webhook-suite/run", new { });
        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());
        var evalRunId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        var eval = (await publisher.WaitForAsync(WebhookEvents.EvalCompleted)).Eval.ShouldNotBeNull();

        eval.EvalRunId.ShouldBe(evalRunId.ToString());
        eval.SuiteName.ShouldBe("webhook-suite");
        eval.AgentName.ShouldBe(TestData.Definition().Name);
        eval.Status.ShouldBe(nameof(EvalRunStatus.Completed));
        eval.Total.ShouldBe(2);
        (eval.Passed + eval.Failed).ShouldBe(2);
    }

    private static Task<TraconTestHost> StartWorkerAsync(RecordingPublisher publisher)
        => TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(50);
                });
            },
            configureServices: services =>
            {
                services.AddSingleton<IWebhookPublisher>(publisher);
            });

    private static async Task<Guid> TriggerBatchAsync(TraconTestHost host, string agentName)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/schedules/webhook-probe", UriKind.Relative),
                   new JobScheduleSaveRequest
                   {
                       HandlerKey = JobHandlerKeys.AgentBatch,
                       TargetName = agentName,
                       Cron = "0 3 * * *",
                       TimeZone = "UTC",
                       Payload = JsonDocument.Parse("""["hello"]""").RootElement,
                       Enabled = true,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/schedules/webhook-probe/trigger", UriKind.Relative),
            new { });

        triggered.EnsureSuccessStatusCode();

        return (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();
    }

    private static async Task RunToEndAsync(TraconTestHost host, string route)
    {
        using var response = await host.Client.PostAsJsonAsync(route, new WorkflowRunHttpRequest { Message = "go" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _ = await response.Content.ReadAsStringAsync();
    }

    private static Workflow BuildApprovalWorkflow()
    {
        var port = RequestPort.Create<string, bool>("approval-port");
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var end = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "approved" : "rejected",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, portBinding)
            .AddEdge(portBinding, end)
            .WithOutputFrom(end)
            .WithName("approval-flow")
            .Build();
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        private readonly List<(string EventType, WebhookEventPayload Payload)> _events = [];

        public IReadOnlyList<(string EventType, WebhookEventPayload Payload)> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add((eventType, payload));
            }

            return new ValueTask<int>(1);
        }

        public async Task<WebhookEventPayload> WaitForAsync(string eventType, Func<WebhookEventPayload, bool>? match = null)
        {
            WebhookEventPayload? found = null;

            await WaitUntil.TrueAsync(
                () =>
                {
                    found = Events
                        .Where(entry => string.Equals(entry.EventType, eventType, StringComparison.Ordinal))
                        .Select(static entry => entry.Payload)
                        .FirstOrDefault(payload => match is null || match(payload));

                    return found is not null;
                },
                $"a '{eventType}' webhook");

            return found!;
        }
    }
}
