using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure modes 1 and 2: a queued workflow operation passes the same gates as a streamed
/// one, in the same order, BEFORE its <c>202</c>; a refusal leaves no job and no row.
/// </summary>
/// <remarks>
/// The worker does not see the request (K-894): a gate skipped at queue time is never
/// asked. The order is attribution, authorization, session ownership, quota (a run
/// only, K-394), then parameters; a refusal at one gate must not consume the next.
/// </remarks>
public sealed class WorkflowAsyncRunGateTests
{
    private const string RunPath = $"/api/workflows/{Recoverable}/run";

    [Fact]
    public async Task An_oversized_label_set_is_refused_with_400_before_anything_is_queued()
    {
        var labels = Enumerable.Range(0, RunLabels.MaxCount + 1)
            .ToDictionary(static index => $"key-{index}", static _ => "value", StringComparer.Ordinal);
        var attribution = new SettableAttribution { UserId = "ada", Labels = labels };
        var authorization = new RecordingAuthorization();

        await using var host = await StartGatedAsync(attribution, authorization);

        using var refused = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 7" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        authorization.Requests.ShouldBe(0, "attribution is checked before the authorization handler is asked");
        await ShouldHaveQueuedNothingAsync(host);
    }

    /// <summary>Failure mode 2: a denying handler gets 403 on all three endpoints, never a 202.</summary>
    [Fact]
    public async Task A_denying_authorization_handler_refuses_a_queued_run_resume_and_response_with_403()
    {
        var authorization = new RecordingAuthorization();
        var probe = new RecoveryProbe();

        await using var host = await StartGatedAsync(new SettableAttribution(), authorization, probe, runWorker: true);
        await using var lifetime = probe;

        // A source run to resume, and one waiting for an answer, written while allowed.
        using var first = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 7" });
        var sourceId = await AcceptedRunIdAsync(first);
        await host.WaitForRunStatusAsync(sourceId, nameof(RunStatus.Completed));

        using var waiting = await PostAsync(host, $"/api/workflows/{Approval}/run", new WorkflowRunHttpRequest { Message = "the draft" });
        var waitingId = await AcceptedRunIdAsync(waiting);
        await host.WaitForRunStatusAsync(waitingId, nameof(RunStatus.AwaitingInput));

        var requestId = await PendingRequestIdAsync(host, waitingId);
        var jobsBefore = (await JobsAsync(host)).Count;
        var runsBefore = (await WorkflowRunsAsync(host)).Count;

        authorization.Deny = true;

        using (var run = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 8" }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var resume = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest()))
        {
            resume.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var respond = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", new { requestId, approved = true }))
        {
            respond.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await JobsAsync(host)).Count.ShouldBe(jobsBefore);
        (await WorkflowRunsAsync(host)).Count.ShouldBe(runsBefore);
    }

    /// <summary>A denied run does not consume the tenant's quota: authorization comes first.</summary>
    [Fact]
    public async Task A_denied_queued_run_is_refused_before_the_quota_gate()
    {
        var authorization = new RecordingAuthorization();
        var probe = new RecoveryProbe();

        await using var host = await StartGatedAsync(new SettableAttribution(), authorization, probe, runWorker: true);
        await using var lifetime = probe;
        await SaveQuotaAsync(host);

        // One run fills the quota of one.
        using (var first = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 7" }))
        {
            await host.WaitForRunStatusAsync(await AcceptedRunIdAsync(first), nameof(RunStatus.Completed));
        }

        authorization.Deny = true;

        using var denied = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 8" });

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "authorization answers before the quota");
    }

    [Fact]
    public async Task A_queued_run_over_the_quota_is_refused_with_429_and_queues_nothing()
    {
        var probe = new RecoveryProbe();

        await using var host = await StartGatedAsync(new SettableAttribution(), new RecordingAuthorization(), probe, runWorker: true);
        await using var lifetime = probe;
        await SaveQuotaAsync(host);

        using (var first = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 7" }))
        {
            await host.WaitForRunStatusAsync(await AcceptedRunIdAsync(first), nameof(RunStatus.Completed));
        }

        var jobsBefore = (await JobsAsync(host)).Count;

        // An undeclared parameter too: the quota answers before the parameter check.
        using var refused = await PostAsync(
            host,
            RunPath,
            new WorkflowRunHttpRequest
            {
                Message = "episode 8",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["undeclared"] = "x" },
            });

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await JobsAsync(host)).Count.ShouldBe(jobsBefore);
    }

    [Fact]
    public async Task An_undeclared_parameter_is_refused_with_400_and_queues_nothing()
    {
        await using var host = await StartGatedAsync(new SettableAttribution(), new RecordingAuthorization());

        using var refused = await PostAsync(
            host,
            RunPath,
            new WorkflowRunHttpRequest
            {
                Message = "episode 7",
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["undeclared"] = "x" },
            });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(refused)).GetProperty("unknownParameters")
            .EnumerateArray().Select(static name => name.GetString()).ShouldBe(["undeclared"]);
        await ShouldHaveQueuedNothingAsync(host);
    }

    /// <summary>A named session the run would open needs an owner when ownership is on, as on the streamed path.</summary>
    [Fact]
    public async Task A_new_session_with_no_owner_is_refused_with_403_when_ownership_is_on()
    {
        await using var host = await StartGatedAsync(
            new SettableAttribution(),
            new RecordingAuthorization(),
            configureServices: static services => services.Configure<TraconSessionOwnershipOptions>(static options =>
            {
                options.Enabled = true;
                options.RequireAuthenticatedOwner = true;
            }));

        using var refused = await PostAsync(host, RunPath, new WorkflowRunHttpRequest { Message = "episode 7", SessionId = "production-7" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{(int)refused.StatusCode}: {await refused.Content.ReadAsStringAsync()}");
        await ShouldHaveQueuedNothingAsync(host);
    }

    [Fact]
    public async Task An_unknown_workflow_is_404_and_queues_nothing()
    {
        await using var host = await StartGatedAsync(new SettableAttribution(), new RecordingAuthorization());

        using var refused = await PostAsync(host, "/api/workflows/no-such-workflow/run", new WorkflowRunHttpRequest { Message = "x" });

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await ShouldHaveQueuedNothingAsync(host);
    }

    private static async Task<TraconTestHost> StartGatedAsync(
        SettableAttribution attribution,
        RecordingAuthorization authorization,
        RecoveryProbe? probe = null,
        bool runWorker = false,
        Action<IServiceCollection>? configureServices = null)
    {
        var host = await StartAsync(
            probe ?? new RecoveryProbe(),
            runWorker: runWorker,
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(attribution));
                services.AddSingleton<IRunAuthorizationHandler>(authorization);
                configureServices?.Invoke(services);
            });

        await SaveRecoverableAsync(host);

        return host;
    }

    private static async Task SaveQuotaAsync(TraconTestHost host)
    {
        using var created = await host.Client.PutAsJsonAsync(
            "/tracon/api/quotas",
            new QuotaSaveRequest { AgentName = Recoverable, Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true });

        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
    }

    private static async Task<string> PendingRequestIdAsync(TraconTestHost host, Guid runId)
    {
        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/runs/{runId}/requests");

        return (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!;
    }

    private static async Task ShouldHaveQueuedNothingAsync(TraconTestHost host)
    {
        (await JobsAsync(host)).ShouldBeEmpty();
        (await WorkflowRunsAsync(host)).ShouldBeEmpty();
    }

    /// <summary>Allows until told to deny; counts what it was asked.</summary>
    private sealed class RecordingAuthorization : IRunAuthorizationHandler
    {
        private int _requests;

        public bool Deny { get; set; }

        public int Requests => Volatile.Read(ref _requests);

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _requests);

            return ValueTask.FromResult(Deny ? RunAuthorizationResult.Deny("not this caller") : RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
