using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// The rationale is the same as in RunReplayEndpointTests.cs (K-269): the
// package's AgentPrism.Testing.AgentPrismTestHost and this project's own
// AgentPrismTestHost SHARE the same name; a blanket `using AgentPrism.Testing;`
// would produce CS0104.
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Tests for the asynchronous approval inbox endpoints (Phase 55).</summary>
public sealed class ApprovalEndpointTests
{
    private const string AgentName = "approval-agent";
    private const string TenantHeader = "X-AgentPrism-Tenant";

    private static readonly Uri Run = new($"/agentprism/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/agentprism/api/approvals/pending", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostQueuedAsync(AgentPrismTestHost host, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Prefer", "respond-async");

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private static void ConfigureApprovalAgent(IAgentPrismBuilder builder)
    {
        builder
            .AddModelProvider(new FakeModelProvider("approval-model")
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
                Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                ToolNames = ["cancel_order"],
            });
    }

    /// <summary>
    /// Polls a run until it reaches the expected status.
    /// </summary>
    /// <remarks>
    /// 🚨 If the timeout expires, it fails HERE. The earlier version silently
    /// returned the last status it saw; where the caller did not check it (see
    /// lines 140, 192), the test continued and failed on an UNRELATED assertion
    /// ("pending approval list is empty"). Measured with the full suite running:
    /// 5 sec is not enough with 16 test projects running in parallel; the
    /// package, which passes 447/447 alone, produced 1 failure in the batch run.
    /// The timeout was raised to 30 sec (still takes only milliseconds under a
    /// healthy run, generously, even under load).
    /// </remarks>
    private static async Task<string> WaitForStatusAsync(AgentPrismTestHost host, Guid runId, string expected)
    {
        var uri = new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? status = null;

        while (DateTime.UtcNow < deadline)
        {
            using var poll = await host.Client.GetAsync(uri);
            status = (await AgentPrismTestHost.ReadJsonAsync(poll)).GetProperty("status").GetString();

            if (string.Equals(status, expected, StringComparison.Ordinal))
            {
                // `expected` is not null; if equality held, `status` is not null either.
                return status!;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException(
            $"Run {runId} did not reach status '{expected}' within 30 seconds; last seen status: '{status}'.");
    }

    [Fact]
    public async Task Queued_run_requests_approval_is_approved_from_the_console_and_completes()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "session-1" });

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var originalRunId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var pending = (await AgentPrismTestHost.ReadJsonAsync(pendingResponse)).EnumerateArray().ShouldHaveSingleItem();

        pending.GetProperty("toolName").GetString().ShouldBe("cancel_order");
        pending.GetProperty("runId").GetGuid().ShouldBe(originalRunId);
        pending.GetProperty("status").GetString().ShouldBe("Pending");

        var approvalId = pending.GetProperty("id").GetGuid();

        using var decideResponse = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        decideResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var decided = await AgentPrismTestHost.ReadJsonAsync(decideResponse);
        decided.GetProperty("status").GetString().ShouldBe("Approved");
        decided.GetProperty("decidedBy").GetString().ShouldNotBeNullOrEmpty();

        // The old run REMAINS AwaitingApproval (K-014); a new run continues with
        // the same session and becomes Completed.
        (await WaitForStatusAsync(host, originalRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        using var runningJobs = await host.Client.GetAsync(new Uri("/agentprism/api/runs?sessionId=session-1", UriKind.Relative));
        var runs = (await AgentPrismTestHost.ReadJsonAsync(runningJobs)).EnumerateArray().ToList();

        runs.Count.ShouldBe(2);

        var resumedRunId = runs
            .Select(static run => run.GetProperty("id").GetGuid())
            .Single(id => id != originalRunId);

        (await WaitForStatusAsync(host, resumedRunId, "Completed")).ShouldBe("Completed");

        using var finalRun = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{resumedRunId}", UriKind.Relative));
        var finalBody = await AgentPrismTestHost.ReadJsonAsync(finalRun);

        finalBody.GetProperty("status").GetString().ShouldBe("Completed");

        using var pendingAfter = await host.Client.GetAsync(PendingApprovals);
        (await AgentPrismTestHost.ReadJsonAsync(pendingAfter)).EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Second_decision_on_the_same_approval_gets_409()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "session-2" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitForStatusAsync(host, runId, "AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var approvalId = (await AgentPrismTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        var decideUri = new Uri($"/agentprism/api/approvals/{approvalId}/decide", UriKind.Relative);

        using (var first = await host.Client.PostAsJsonAsync(decideUri, new ApprovalDecisionRequest { Approved = true }))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var second = await host.Client.PostAsJsonAsync(decideUri, new ApprovalDecisionRequest { Approved = false });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reader_role_cannot_make_a_decision()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/approvals/{Guid.NewGuid()}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_tenants_approval_is_not_visible()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder =>
            {
                ConfigureApprovalAgent(builder);
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            },
            configureServices: static services => services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        using var accepted = await PostQueuedAsync(host, new AgentRunRequest { Message = "cancel the order", SessionId = "session-3" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        await WaitForStatusAsync(host, runId, "AwaitingApproval");

        using var pendingResponse = await host.Client.GetAsync(PendingApprovals);
        var approvalId = (await AgentPrismTestHost.ReadJsonAsync(pendingResponse))
            .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

        using var otherTenantGet = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/agentprism/api/approvals/{approvalId}", UriKind.Relative));
        otherTenantGet.Headers.Add(TenantHeader, "other-tenant");

        using var getResponse = await host.Client.SendAsync(otherTenantGet);

        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var otherTenantDecide = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/agentprism/api/approvals/{approvalId}/decide", UriKind.Relative))
        {
            Content = JsonContent.Create(new ApprovalDecisionRequest { Approved = true }),
        };
        otherTenantDecide.Headers.Add(TenantHeader, "other-tenant");

        using var decideResponse = await host.Client.SendAsync(otherTenantDecide);

        decideResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// HATA-S2-004/MT-MCP-023: previously, only runs going through the queue
    /// (<c>Prefer: respond-async</c>) reflected this state; the synchronous/
    /// non-streaming path silently closed the same awaiting-approval tool call
    /// as <c>Completed</c> (see RunRecordingAgent.RunCoreAsync).
    /// </summary>
    [Fact]
    public async Task Synchronous_non_streaming_run_closes_as_AwaitingApproval_when_it_requests_approval()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureApprovalAgent);

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "cancel the order", SessionId = "session-sync-non-streaming" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var runId = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();

        using var runResponse = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{runId}", UriKind.Relative));
        (await AgentPrismTestHost.ReadJsonAsync(runResponse)).GetProperty("status").GetString()
            .ShouldBe("AwaitingApproval");
    }

    /// <summary>The streaming (SSE) variant of the same defect — RunRecordingAgent.RunCoreStreamingAsync.</summary>
    [Fact]
    public async Task Synchronous_streaming_run_closes_as_AwaitingApproval_when_it_requests_approval()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureApprovalAgent);

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "cancel the order", SessionId = "session-sync-streaming" });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        await response.Content.ReadAsStringAsync();

        using var runningJobs = await host.Client.GetAsync(
            new Uri("/agentprism/api/runs?sessionId=session-sync-streaming", UriKind.Relative));
        var run = (await AgentPrismTestHost.ReadJsonAsync(runningJobs)).EnumerateArray().ShouldHaveSingleItem();

        run.GetProperty("status").GetString().ShouldBe("AwaitingApproval");
    }

    /// <summary>
    /// 🚨 F-133: <see cref="RunStatus.AwaitingApproval"/> is TERMINAL and is the
    /// signal a consumer polls on. It used to be published BEFORE the approval
    /// row existed: <c>RunRecordingAgent</c> closed the run inside
    /// <c>agent.RunAsync()</c> and <c>AgentRunJobHandler</c> wrote the row only
    /// afterwards, so <c>GET /api/approvals/pending</c> could legitimately answer
    /// an empty array for a run that already said "AwaitingApproval". The window
    /// was NOT a race — the order was fixed, so it was open on EVERY queued run;
    /// the flaky part was only whether the consumer looked inside it.
    /// </summary>
    [Fact]
    public async Task Approval_row_exists_before_the_run_reports_AwaitingApproval()
    {
        var probe = new ApprovalOrderProbe();

        await using var host = await AgentPrismTestHost.StartAsync(
            ConfigureApprovalAgent,
            configureServices: services =>
            {
                services.AddSingleton<IPendingApprovalStore>(serviceProvider => probe.Bind(serviceProvider));
                services.UseScheduling(o => o.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        using var accepted = await PostQueuedAsync(
            host, new AgentRunRequest { Message = "cancel the order", SessionId = "session-order" });
        var runId = (await AgentPrismTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();

        (await WaitForStatusAsync(host, runId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        // The contract a consumer sees: the status and a listable approval arrive
        // together. Measured before the fix, this read answered an EMPTY array —
        // the row had not been written yet.
        using var pending = await host.Client.GetAsync(PendingApprovals, TestContext.Current.CancellationToken);
        (await AgentPrismTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem();

        // The mechanism behind it: the row is written while the run is still open.
        probe.RunStatusWhenApprovalWasWritten.ShouldNotBeNull();
        probe.RunStatusWhenApprovalWasWritten.ShouldNotBe(RunStatus.AwaitingApproval);
    }

    /// <summary>
    /// Records the run's status AT THE MOMENT the approval row is written. The
    /// real store is internal to AgentPrism.Core, so this stands in for it; only
    /// the members the approval flow touches carry behaviour.
    /// </summary>
    private sealed class ApprovalOrderProbe : IPendingApprovalStore
    {
        private readonly List<PendingApproval> _approvals = [];
        private IServiceProvider? _services;

        /// <summary>The status seen when the FIRST approval row was written.</summary>
        public RunStatus? RunStatusWhenApprovalWasWritten { get; private set; }

        public ApprovalOrderProbe Bind(IServiceProvider services)
        {
            _services = services;

            return this;
        }

        public async ValueTask CreateAsync(PendingApproval approval, CancellationToken cancellationToken = default)
        {
            var run = await _services!.GetRequiredService<IRunStore>()
                .GetRunAsync(approval.RunId, cancellationToken)
                .ConfigureAwait(false);

            lock (_approvals)
            {
                RunStatusWhenApprovalWasWritten ??= run?.Status;
                _approvals.Add(approval);
            }
        }

        public ValueTask<IReadOnlyList<PendingApproval>> ListPendingAsync(CancellationToken cancellationToken = default)
        {
            lock (_approvals)
            {
                return new ValueTask<IReadOnlyList<PendingApproval>>(
                    _approvals.Where(static a => a.Status == ApprovalStatus.Pending).ToList());
            }
        }

        public ValueTask<PendingApproval?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_approvals)
            {
                return new ValueTask<PendingApproval?>(_approvals.Find(a => a.Id == id));
            }
        }

        public ValueTask<bool> DecideAsync(
            Guid id,
            bool approved,
            string decidedBy,
            DateTimeOffset decidedAt,
            CancellationToken cancellationToken = default)
        {
            lock (_approvals)
            {
                var index = _approvals.FindIndex(a => a.Id == id && a.Status == ApprovalStatus.Pending);

                if (index < 0)
                {
                    return new ValueTask<bool>(false);
                }

                _approvals[index] = _approvals[index] with
                {
                    Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected,
                    DecidedBy = decidedBy,
                    DecidedAt = decidedAt,
                };

                return new ValueTask<bool>(true);
            }
        }

        public ValueTask<IReadOnlyList<PendingApproval>> ExpireAsync(
            DateTimeOffset olderThan,
            int max,
            CancellationToken cancellationToken = default)
            => new(Array.Empty<PendingApproval>());
    }
}
