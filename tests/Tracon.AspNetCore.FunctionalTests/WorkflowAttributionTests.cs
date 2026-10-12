using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow run carries the attribution an agent run carries: its nodes see
/// the run's user and labels, its quota record names the run, and every
/// endpoint that starts a run refuses an attribution that breaks a limit.
/// </summary>
/// <remarks>
/// F-337: the workflow root built its <see cref="AgentRunScope"/> before it
/// read the attribution, so a function node read <c>UserId == null</c>; its
/// quota record carried no run or user, so the <c>quota.threshold</c> webhook
/// named neither; and only the agent run endpoint called
/// <c>RunAttributionGate</c>, so every other run-starting endpoint started an
/// unattributed run with <c>200</c> where the agent endpoint answered
/// <c>400</c>. The scope is an <c>AsyncLocal</c> on a streaming path, so the
/// cases run through the real host.
/// </remarks>
public sealed class WorkflowAttributionTests
{
    private const string Workflow = "attributed";

    [Fact]
    public async Task A_workflow_node_sees_the_runs_user_and_labels()
    {
        AgentRunScope? seen = null;

        await using var host = await StartAsync(
            new StubAttribution("ada", new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" }),
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "probe",
                _ => (messages, _, _) =>
                {
                    seen = TraconRunContext.Current;

                    return new ValueTask<List<ChatMessage>>(messages);
                }));

        await SaveAsync(host, "probe");
        await RunAsync(host);

        seen.ShouldNotBeNull();
        seen.UserId.ShouldBe("ada");
        seen.Labels.ShouldNotBeNull()["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task A_quota_threshold_names_the_workflow_run_and_its_user()
    {
        var publisher = new RecordingPublisher();

        await using var host = await StartAsync(
            new StubAttribution("ada", labels: null),
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "probe",
                _ => (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)),
            services => services.AddSingleton<IWebhookPublisher>(publisher));

        using (var quota = await host.Client.PutAsJsonAsync(
                   "/tracon/api/quotas",
                   new QuotaSaveRequest { AgentName = Workflow, Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            quota.StatusCode.ShouldBe(HttpStatusCode.OK, await quota.Content.ReadAsStringAsync());
        }

        await SaveAsync(host, "probe");
        var run = await RunAsync(host);

        var crossings = publisher.Events
            .Where(static entry => string.Equals(entry.EventType, WebhookEvents.QuotaThreshold, StringComparison.Ordinal))
            .Select(static entry => entry.Payload.Quota.ShouldNotBeNull())
            .ToList();

        crossings.ShouldNotBeEmpty();
        crossings.ShouldAllBe(crossing => crossing.RunId == run.Id.ToString() && crossing.UserId == "ada");
    }

    [Theory]
    [InlineData("/tracon/api/workflows/attributed/run")]
    [InlineData("/tracon/api/workflows/runs/7d9f3c1e-2b8a-4f6d-9e0c-5a1b2c3d4e5f/resume")]
    [InlineData("/tracon/api/workflows/runs/7d9f3c1e-2b8a-4f6d-9e0c-5a1b2c3d4e5f/respond")]
    [InlineData("/tracon/api/runs/7d9f3c1e-2b8a-4f6d-9e0c-5a1b2c3d4e5f/replay")]
    public async Task Too_many_labels_are_refused_before_a_run_starts(string route)
    {
        await using var host = await StartAsync(
            new StubAttribution("ada", TooManyLabels()),
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "probe",
                _ => (messages, _, _) => new ValueTask<List<ChatMessage>>(messages)));

        await SaveAsync(host, "probe");

        using var response = await host.Client.PostAsJsonAsync(
            route,
            new { message = "hello", requestId = "r-1", data = new { approved = true } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync())
            .ShouldContain(RunLabels.MaxCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/tracon/v1/chat/completions")]
    [InlineData("/tracon/v1/responses")]
    public async Task Too_many_labels_are_refused_in_the_openai_error_shape(string route)
    {
        await using var host = await StartAsync(new StubAttribution("ada", TooManyLabels()));

        using var response = await host.Client.PostAsJsonAsync(
            route,
            new
            {
                model = "writer",
                messages = new[] { new { role = "user", content = "hello" } },
                input = "hello",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("error").GetProperty("message").GetString()
            .ShouldNotBeNull()
            .ShouldContain(RunLabels.MaxCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
    }

    private static Dictionary<string, string> TooManyLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < RunLabels.MaxCount + 1; index++)
        {
            labels[$"key-{index}"] = "value";
        }

        return labels;
    }

    private static Task<TraconTestHost> StartAsync(
        IRunAttributionContext attribution,
        Action<ITraconBuilder>? configureWorkflows = null,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddAgent(TestData.Definition("writer"));
                builder.UseWorkflows();
                configureWorkflows?.Invoke(builder);
            },
            configureServices: services =>
            {
                // Registered BEFORE Tracon's own TryAdd, the way a consumer
                // binding its identity pipeline would (the consumer wins).
                services.Replace(ServiceDescriptor.Singleton(attribution));
                configureServices?.Invoke(services);
            });

    private static async Task SaveAsync(TraconTestHost host, string function)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{Workflow}",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = function, Kind = WorkflowNodeKind.Function },
                ],
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task<RunRecord> RunAsync(TraconTestHost host)
    {
        using (var response = await host.Client.PostAsJsonAsync(
                   $"/tracon/api/workflows/{Workflow}/run",
                   new WorkflowRunHttpRequest { Message = "hello" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            _ = await response.Content.ReadAsStringAsync();
        }

        return (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(static run => run.Kind == RunKind.Workflow);
    }

    private sealed class StubAttribution(string? userId, IReadOnlyDictionary<string, string>? labels)
        : IRunAttributionContext
    {
        public string? UserId => userId;

        public IReadOnlyDictionary<string, string>? Labels => labels;
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        public List<(string EventType, WebhookEventPayload Payload)> Events { get; } = [];

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            lock (Events)
            {
                Events.Add((eventType, payload));
            }

            return new ValueTask<int>(1);
        }
    }
}
