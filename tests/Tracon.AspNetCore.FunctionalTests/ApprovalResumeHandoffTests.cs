using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// K-269: Tracon.Testing.TraconTestHost and this project's own host
// share a name, so the package is imported one type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The window between applying an approval decision and handing the run off to
/// the worker (B03).
/// </summary>
/// <remarks>
/// <para>
/// Deciding an approval is four writes in a row: audit, <c>DecideAsync</c>,
/// <c>StartRunAsync</c>, <c>EnqueueAsync</c>. Nothing spans them, so a failure
/// after the second one left the approval decided with no run to carry it out.
/// Retrying used to answer <c>409 AlreadyDecided</c> — the decision was made,
/// the work never happened, and no operator action could recover it. Run
/// reconciliation does not help: it only claims runs already <c>Running</c>,
/// and is off by default.
/// </para>
/// <para>
/// The fix makes the handoff re-drivable rather than transactional: the resume
/// run's identity is derived from the approval instead of being random, so
/// repeating the same decision rebuilds exactly the same run and job rather
/// than a second one. A repeat that asks for the OPPOSITE decision is still a
/// conflict — see <c>ApprovalEndpointTests.Second_decision_on_the_same_approval_gets_409</c>.
/// </para>
/// </remarks>
public sealed class ApprovalResumeHandoffTests
{
    private const string AgentName = "handoff-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);

    [Fact]
    public async Task A_decision_whose_handoff_failed_is_completed_by_repeating_it()
    {
        var queue = new FailFirstResumeEnqueue();

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                ConfigureApprovalAgent(builder);
                queue.Install(builder.Services);
            },
            configureServices: static services =>
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest
        {
            Message = "cancel the order",
            SessionId = "handoff-session",
        });

        var originalRunId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitForStatusAsync(host, originalRunId, "AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var approvalId = (await TraconTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        var decideUri = new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative);

        // The decision is applied, then the handoff blows up. TestServer
        // rethrows an unhandled exception rather than turning it into a 500,
        // so the crash surfaces here as the throw itself.
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await host.Client.PostAsJsonAsync(
                decideUri, new ApprovalDecisionRequest { Approved = true }));

        queue.Failures.ShouldBe(1, "the injected failure never fired, so this test proves nothing.");

        // Repeating the SAME decision must finish the handoff rather than
        // reporting a conflict over a decision whose work never started.
        using (var retried = await host.Client.PostAsJsonAsync(
            decideUri, new ApprovalDecisionRequest { Approved = true }))
        {
            retried.StatusCode.ShouldBe(HttpStatusCode.OK, await retried.Content.ReadAsStringAsync());
        }

        using var runs = await host.Client.GetAsync(
            new Uri("/tracon/api/runs?sessionId=handoff-session", UriKind.Relative));

        var resumed = (await TraconTestHost.ReadJsonAsync(runs))
            .EnumerateArray()
            .Select(static run => run.GetProperty("id").GetGuid())
            .Where(id => id != originalRunId)
            .ToList();

        // Exactly one: a retry that minted a fresh identifier would leave the
        // abandoned first run behind as well.
        resumed.Count.ShouldBe(1);

        (await WaitForStatusAsync(host, resumed[0], "Completed")).ShouldBe("Completed");
    }

    private static async Task<HttpResponseMessage> PostQueuedAsync(TraconTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private static void ConfigureApprovalAgent(ITraconBuilder builder)
        => builder
            .AddModelProvider(new FakeModelProvider("handoff-model")
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} canceled."),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: options => options.RequiresApproval = true)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "handoff-model", Model = "handoff-1" },
                ToolNames = ["cancel_order"],
            });

    private static async Task<string> WaitForStatusAsync(TraconTestHost host, Guid runId, string expected)
    {
        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var poll = await host.Client.GetAsync(uri);
            status = (await TraconTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                return expected;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Run {runId} was '{status}', not '{expected}', within 30 seconds.");
    }

    /// <summary>
    /// Fails the first attempt to queue an approval-resume job, then behaves
    /// normally — the crash-after-decide window, made reproducible.
    /// </summary>
    private sealed class FailFirstResumeEnqueue
    {
        private int _fired;

        public int Failures => _fired;

        /// <summary>
        /// Wraps whatever job store is already registered. Called through
        /// <c>configureTracon</c>, so Tracon's own
        /// <c>TryAddSingleton&lt;IJobStore&gt;</c> has already run and there is
        /// a real implementation to delegate to.
        /// </summary>
        public void Install(IServiceCollection services)
        {
            var registered = services.Last(static d => d.ServiceType == typeof(IJobStore));

            services.Remove(registered);
            services.AddSingleton<IJobStore>(provider => new Store(
                this,
                (IJobStore)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)));
        }

        private bool ShouldFail(JobRecord job)
            => string.Equals(job.HandlerKey, JobHandlerKeys.ApprovalResume, StringComparison.Ordinal)
               && Interlocked.CompareExchange(ref _fired, 1, 0) == 0;

        private sealed class Store(FailFirstResumeEnqueue owner, IJobStore inner) : IJobStore
        {
            public ValueTask<JobRecord> EnqueueAsync(
                JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
                => owner.ShouldFail(job)
                    ? throw new InvalidOperationException("Injected: the queue is unavailable.")
                    : inner.EnqueueAsync(job, items, cancellationToken);

            public ValueTask<JobRecord?> LeaseAsync(
                string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes,
                CancellationToken cancellationToken = default)
                => inner.LeaseAsync(owner, leaseDuration, lanes, cancellationToken);

            public ValueTask RenewLeaseAsync(
                Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
                => inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

            public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
                => inner.MarkRunningAsync(jobId, owner, cancellationToken);

            public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
                => inner.CompleteAsync(completion, cancellationToken);

            public ValueTask ReleaseForRetryAsync(
                Guid jobId, string errorMessage, TimeSpan? retryAfter = null,
                CancellationToken cancellationToken = default)
                => inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

            public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
                => inner.CancelAsync(tenantId, jobId, cancellationToken);

            public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
                => inner.GetAsync(tenantId, jobId, cancellationToken);

            public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
                => inner.QueryAsync(query, cancellationToken);

            public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
                => inner.ListItemsAsync(jobId, cancellationToken);

            public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
                => inner.ReportItemAsync(item, cancellationToken);

            public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
                => inner.GetQueueDepthAsync(cancellationToken);
        }
    }
}
