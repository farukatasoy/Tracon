using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow or a batch that runs from the queue carries the attribution of
/// whoever queued it: its nodes see that user and those labels, and so do its
/// run rows.
/// </summary>
/// <remarks>
/// F-364: <c>WorkflowJobHandler</c> opened no <see cref="AmbientRunAttributionScope"/>,
/// and nothing that queued a workflow job wrote an attribution into it. A
/// workflow started by an inbound trigger, a schedule or <see cref="IJobDispatcher"/>
/// ran with no user and no labels, although the trigger accept endpoint had
/// checked them (F-337). The same held for an agent batch, and for an agent run
/// an inbound trigger starts. The scope is an <c>AsyncLocal</c> read on a
/// streaming path in a background worker, so the cases run through the real host.
/// </remarks>
public sealed class QueuedWorkflowAttributionTests
{
    private const string Workflow = "attributed";
    private const string SecretConfigurationKey = "Tracon:TriggerSecrets:Hooks";
    private const string Secret = "whsec_attribution";

    private static readonly IReadOnlyDictionary<string, string> Team =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" };

    [Fact]
    public async Task A_workflow_queued_under_an_attribution_scope_runs_with_its_user_and_labels()
    {
        var probe = new Probe();

        await using var host = await StartAsync(probe);
        await SaveWorkflowAsync(host);

        var dispatcher = host.Services.GetRequiredService<IJobDispatcher>();
        JobRecord job;

        using (AmbientRunAttributionScope.Begin("ada", Team))
        {
            job = await dispatcher.EnqueueAsync(new JobRequest
            {
                TenantId = TenantOf(host),
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = Workflow,
                Items = ["hello"],
            });
        }

        (await WaitForJobAsync(host, job.Id)).ShouldBe(JobStatus.Completed);

        probe.Seen.ShouldNotBeNull();
        probe.Seen.UserId.ShouldBe("ada");
        probe.Seen.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");

        var root = await WorkflowRootAsync(host);
        root.UserId.ShouldBe("ada");
        root.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task A_workflow_started_by_an_inbound_trigger_runs_with_the_requests_attribution()
    {
        var probe = new Probe();

        await using var host = await StartAsync(probe, requestBoundAttribution: true);
        await SaveWorkflowAsync(host);
        await SaveTriggerAsync(host, "workflow", Workflow);

        using var accepted = await SendSignedAsync(host, user: "ada", team: "payments");

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("jobId").GetGuid();

        (await WaitForJobAsync(host, jobId)).ShouldBe(JobStatus.Completed);

        probe.Seen.ShouldNotBeNull();
        probe.Seen.UserId.ShouldBe("ada");
        probe.Seen.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task An_agent_run_started_by_an_inbound_trigger_records_the_requests_attribution()
    {
        await using var host = await StartAsync(new Probe(), requestBoundAttribution: true);
        await SaveTriggerAsync(host, "agent", "writer");

        using var accepted = await SendSignedAsync(host, user: "ada", team: "payments");

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());

        var runId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.Completed));

        var run = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(candidate => candidate.Id == runId);

        run.UserId.ShouldBe("ada");
        run.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task A_scheduled_workflow_runs_with_the_attribution_its_payload_names()
    {
        var probe = new Probe();

        await using var host = await StartAsync(probe);
        await SaveWorkflowAsync(host);

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/schedules/attributed-nightly",
                   new JobScheduleSaveRequest
                   {
                       HandlerKey = JobHandlerKeys.Workflow,
                       TargetName = Workflow,
                       Payload = JsonDocument.Parse("""{"items":["hello"],"userId":"ada","labels":{"team":"payments"}}""").RootElement,
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var triggered = await host.Client.PostAsJsonAsync("/tracon/api/schedules/attributed-nightly/trigger", new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK, await triggered.Content.ReadAsStringAsync());

        var jobId = (await TraconTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        (await WaitForJobAsync(host, jobId)).ShouldBe(JobStatus.Completed);

        // The envelope's items are the job's items; the envelope is never run as one.
        (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(jobId))
            .ShouldHaveSingleItem().Input.ShouldBe("hello");

        probe.Seen.ShouldNotBeNull();
        probe.Seen.UserId.ShouldBe("ada");
        probe.Seen.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task A_schedule_whose_payload_breaks_an_attribution_limit_is_refused()
    {
        await using var host = await StartAsync(new Probe());

        var labels = string.Join(
            ",",
            Enumerable.Range(0, RunLabels.MaxCount + 1).Select(static index => $"\"key-{index}\":\"value\""));

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/schedules/too-many-labels",
            new JobScheduleSaveRequest
            {
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = Workflow,
                Payload = JsonDocument.Parse("""{"items":["hello"],"labels":{""" + labels + "}}").RootElement,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadAsStringAsync()).ShouldContain(RunLabels.MaxCount.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task An_agent_batch_queued_under_an_attribution_scope_records_its_user_and_labels()
    {
        await using var host = await StartAsync(new Probe());

        var dispatcher = host.Services.GetRequiredService<IJobDispatcher>();
        JobRecord job;

        using (AmbientRunAttributionScope.Begin("ada", Team))
        {
            job = await dispatcher.EnqueueAsync(new JobRequest
            {
                TenantId = TenantOf(host),
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "writer",
                Items = ["hello"],
            });
        }

        (await WaitForJobAsync(host, job.Id)).ShouldBe(JobStatus.Completed);

        var item = (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(job.Id)).ShouldHaveSingleItem();

        var run = (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(candidate => candidate.Id == item.RunId);

        run.UserId.ShouldBe("ada");
        run.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    private static Task<TraconTestHost> StartAsync(Probe probe, bool requestBoundAttribution = false)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddAgent(TestData.Definition("writer"));
                builder.UseWorkflows();
                builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "probe",
                    _ => (messages, _, _) =>
                    {
                        probe.Seen = TraconRunContext.Current;

                        return new ValueTask<List<ChatMessage>>(messages);
                    });
            },
            configureServices: services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection([new(SecretConfigurationKey, Secret)])
                    .Build());
                services.Configure<TraconSchedulingOptions>(static options => options.PollInterval = TimeSpan.FromMilliseconds(50));

                if (requestBoundAttribution)
                {
                    // Registered BEFORE Tracon's own TryAdd, the way a consumer
                    // binding its identity pipeline would (the consumer wins).
                    services.AddHttpContextAccessor();
                    services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext, RequestOrAmbientAttribution>());
                }
            });

    private static string TenantOf(TraconTestHost host)
        => host.Services.GetRequiredService<ITenantContext>().TenantId;

    private static async Task SaveWorkflowAsync(TraconTestHost host)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{Workflow}",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "probe", Kind = WorkflowNodeKind.Function },
                ],
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task SaveTriggerAsync(TraconTestHost host, string targetKind, string targetName)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/triggers/hooks",
            new { targetKind, targetName, signingSecretConfigurationName = SecretConfigurationKey });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(TraconTestHost host, string user, string team)
    {
        const string body = """{"text":"hello"}""";

        var timestamp = DateTimeOffset.UtcNow;
        var tenant = TenantOf(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/triggers/{tenant}/hooks")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSigner.SignatureHeader, WebhookSigner.Sign(body, timestamp, Secret));
        request.Headers.Add(RequestOrAmbientAttribution.UserHeader, user);
        request.Headers.Add(RequestOrAmbientAttribution.TeamHeader, team);

        return await host.Client.SendAsync(request);
    }

    private static async Task<JobStatus> WaitForJobAsync(TraconTestHost host, Guid jobId)
    {
        var store = host.Services.GetRequiredService<IJobStore>();
        var tenant = TenantOf(host);

        var job = await WaitUntil.ValueAsync(
            async () => (await store.GetAsync(tenant, jobId))!,
            static current => current.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            "the queued job to finish");

        return job.Status;
    }

    private static async Task<RunRecord> WorkflowRootAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(static run => run.Kind == RunKind.Workflow);

    /// <summary>Keeps the run context the probe function node saw.</summary>
    /// <remarks>Read only after the job is seen finished, which happens after the node ran.</remarks>
    private sealed class Probe
    {
        public AgentRunScope? Seen { get; set; }
    }

    /// <summary>
    /// A consumer binding: the identity of the current request, and the ambient
    /// scope where there is no request (a background worker).
    /// </summary>
    private sealed class RequestOrAmbientAttribution(IHttpContextAccessor accessor) : IRunAttributionContext
    {
        public const string UserHeader = "X-Test-User";
        public const string TeamHeader = "X-Test-Team";

        public string? UserId
            => accessor.HttpContext is { } http
                ? http.Request.Headers.TryGetValue(UserHeader, out var user) ? user.ToString() : null
                : AmbientRunAttributionScope.CurrentUserId;

        public IReadOnlyDictionary<string, string>? Labels
            => accessor.HttpContext is { } http
                ? http.Request.Headers.TryGetValue(TeamHeader, out var team)
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = team.ToString() }
                    : null
                : AmbientRunAttributionScope.CurrentLabels;
    }
}
