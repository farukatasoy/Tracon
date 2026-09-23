using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ApprovalEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 139 (F-185) over HTTP: an installation's own <see cref="IRunAuthorizationHandler"/>
/// gates run start and session access, across every surface that starts a run.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The single most important claim under test is coverage: the four
/// run-starting surfaces (<c>POST /api/agents/{name}/run</c>, <c>POST
/// /api/workflows/{name}/run</c>, the inbound trigger accept endpoint, and
/// <c>POST /v1/responses</c>) do NOT share one endpoint filter — each calls
/// <c>RunAuthorizationGate</c> explicitly in its own body. A surface that
/// forgot the call would look identical to every other test passing; only a
/// test that exercises THAT surface catches the gap. Every surface therefore
/// gets its own denial test below, not one shared parametrized test.
/// </para>
/// <para>
/// These live at the FUNCTIONAL level on purpose, the same rationale
/// <see cref="RunAttributionEndpointTests"/> documents: the claim is that the
/// handler is wired in FRONT of the run/session, not that the gate class
/// itself works in isolation.
/// </para>
/// </remarks>
public sealed class RunAuthorizationEndpointTests
{
    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Sessions = new("/tracon/api/sessions", UriKind.Relative);

    // --- Case 1: nothing registered, nothing changes ---

    [Fact]
    public async Task Nothing_changes_when_no_handler_is_registered()
    {
        await using var host = await StartAsync(handler: null);

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bare_setup_reports_the_run_authorization_handler_as_built_in_default()
    {
        await using var host = await StartAsync(
            handler: null, configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var body = await TraconTestHost.ReadJsonAsync(
            await host.Client.GetAsync(new Uri("/tracon/api/diagnostics", UriKind.Relative)));

        var point = body.GetProperty("extensionPoints").EnumerateArray()
            .Single(static p => string.Equals(p.GetProperty("contract").GetString(), "IRunAuthorizationHandler", StringComparison.Ordinal));

        point.GetProperty("isBuiltInDefault").GetBoolean().ShouldBeTrue();
        point.GetProperty("implementation").GetString().ShouldBe("AllowAllRunAuthorizationHandler");
    }

    // --- Case 2/3: a run is allowed or denied by user identity ---

    [Fact]
    public async Task Handler_allows_the_expected_user()
    {
        await using var host = await StartAsync(AllowOnly("a"), attribution: "a");

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Handler_denies_a_different_user_and_no_run_row_opens()
    {
        await using var host = await StartAsync(AllowOnly("a"), attribution: "b");

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Run not authorized");

        // 🚨 A denied run must not open a 'runs' row: the caller never
        // started a billable run, and the recorded history must reflect that.
        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_denies_a_run_against_another_users_session()
    {
        var handler = AllowOnly("a");
        await using var host = await StartAsync(handler, attribution: "b");

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "session-of-a" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var request = handler.RunRequests.ShouldHaveSingleItem();
        request.SessionId.ShouldBe("session-of-a");
        request.UserId.ShouldBe("b");
    }

    [Fact]
    public async Task Throwing_handler_denies_the_run_fail_closed()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down"),
        };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var runs = host.Services.GetRequiredService<IRunStore>();
        (await runs.QueryRunsAsync(new RunQuery())).ShouldBeEmpty();
    }

    // --- A throwing handler is a FAILED check, not a decision: logged, and said so ---

    [Fact]
    public async Task Throwing_handler_is_logged_and_the_body_reports_a_failed_check_not_a_denial()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down"),
        };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        var problem = await TraconTestHost.ReadJsonAsync(response);

        // 🚨 The handler never DECIDED anything. A body that says "denied"
        // sends the client and the support desk after a policy rule that does
        // not exist, while the real fault stays invisible.
        problem.GetProperty("title").GetString().ShouldBe("Run not authorized");
        problem.GetProperty("detail").GetString().ShouldBe(FailedCheckDetail);
        body.ShouldNotContain("policy store is down");

        var error = GateErrors(host).ShouldHaveSingleItem();
        error.ShouldContain(nameof(ConfigurableRunAuthorizationHandler));
        error.ShouldContain(nameof(RunAccess.Start));
        error.ShouldContain("kod-agent");
        error.ShouldContain("policy store is down");
    }

    [Fact]
    public async Task Throwing_handler_on_the_session_list_reports_a_failed_check_and_is_logged()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnSession = static _ => throw new InvalidOperationException("the consumer's own policy store is down"),
        };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.GetAsync(Sessions);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldBe(FailedCheckDetail);

        GateErrors(host).ShouldHaveSingleItem().ShouldContain(nameof(SessionAccess.List));
    }

    [Fact]
    public async Task Throwing_handler_on_a_single_session_keeps_the_missing_session_body_and_is_logged()
    {
        var (host, handler, sessionId) = await StartWithSessionAsync();
        await using var _ = host;

        handler.OnSession = static _ => throw new InvalidOperationException("the consumer's own policy store is down");

        using var denied = await host.Client.GetAsync(new Uri($"/tracon/api/sessions/{sessionId}", UriKind.Relative));
        using var missing = await host.Client.GetAsync(new Uri("/tracon/api/sessions/does-not-exist-at-all", UriKind.Relative));

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 The identity-hiding 404 does not change: a body that said "the
        // check failed" would confirm the session exists.
        (await denied.Content.ReadAsStringAsync()).Replace(sessionId, "{id}", StringComparison.Ordinal)
            .ShouldBe((await missing.Content.ReadAsStringAsync()).Replace("does-not-exist-at-all", "{id}", StringComparison.Ordinal));

        // The session gate asks the handler before the store lookup, so the
        // missing session's request wrote its own line as well.
        var error = GateErrors(host).Where(line => line.Contains(sessionId, StringComparison.Ordinal)).ShouldHaveSingleItem();
        error.ShouldContain(nameof(SessionAccess.Read));
    }

    [Fact]
    public async Task Throwing_attribution_is_logged_once_per_request_and_the_caller_has_no_identity()
    {
        var handler = new ConfigurableRunAuthorizationHandler();

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Register(services, handler);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new ThrowingAttribution()));
            });

        using var response = await host.Client.GetAsync(Sessions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Fail-closed direction: an identity that cannot be read is NO
        // identity, never a guessed one.
        handler.SessionRequests.ShouldHaveSingleItem().UserId.ShouldBeNull();

        GateErrors(host).ShouldHaveSingleItem().ShouldContain(nameof(IRunAttributionContext));
    }

    /// <summary>
    /// With session ownership on, one list request reads the identity in two
    /// gates. The failure is one fault, so it is one log line.
    /// </summary>
    [Fact]
    public async Task Throwing_attribution_is_logged_once_even_when_two_gates_read_it()
    {
        var handler = new ConfigurableRunAuthorizationHandler();

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Register(services, handler);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new ThrowingAttribution()));
                services.Configure<TraconSessionOwnershipOptions>(static options => options.Enabled = true);
            });

        using var response = await host.Client.GetAsync(Sessions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GateErrors(host).ShouldHaveSingleItem().ShouldContain(nameof(IRunAttributionContext));
    }

    /// <summary>
    /// A consumer attribution whose own call timed out throws a cancellation
    /// the request did not ask for. That is a failed identity, not a 500.
    /// </summary>
    [Fact]
    public async Task Attribution_that_times_out_is_a_failed_identity_not_a_server_error()
    {
        var handler = new ConfigurableRunAuthorizationHandler();

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Register(services, handler);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new TimingOutAttribution()));
            });

        using var response = await host.Client.GetAsync(Sessions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.SessionRequests.ShouldHaveSingleItem().UserId.ShouldBeNull();
        GateErrors(host).ShouldHaveSingleItem().ShouldContain(nameof(IRunAttributionContext));
    }

    // --- Order: authorization runs BEFORE the quota check ---

    [Fact]
    public async Task Denied_run_does_not_consume_the_quota()
    {
        var handler = new ConfigurableRunAuthorizationHandler { OnRun = static _ => RunAuthorizationResult.Deny("no") };

        await using var host = await StartAsync(handler);

        using (var created = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/quotas", UriKind.Relative),
            new QuotaSaveRequest { AgentName = "kod-agent", Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The quota's OWN counter never moved: had the denied call consumed
        // it, this check (limit 1) would now report it as exhausted.
        var enforcer = host.Services.GetRequiredService<QuotaEnforcer>();
        var decision = await enforcer.CheckAsync("default", "kod-agent");

        decision.IsAllowed.ShouldBeTrue();
    }

    // --- Case: a queued (Prefer: respond-async) run is covered too ---

    [Fact]
    public async Task Queued_run_is_denied_before_it_is_queued()
    {
        var handler = new ConfigurableRunAuthorizationHandler { OnRun = static _ => RunAuthorizationResult.Deny("no") };

        await using var host = await StartAsync(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var jobs = host.Services.GetRequiredService<IJobStore>();
        (await jobs.QueryAsync(new JobQuery())).ShouldBeEmpty();
    }

    // --- Session access: List is rejected (403), everything else looks like 404 ---

    [Fact]
    public async Task Denied_session_list_returns_403()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnSession = static request => request.Access == SessionAccess.List
                ? RunAuthorizationResult.Deny("no")
                : RunAuthorizationResult.Allow(),
        };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.GetAsync(Sessions);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Denied_session_read_returns_404_not_403()
    {
        // Establish a session with a real run first, so a passing test would
        // otherwise ALSO get 404 from "does not exist" - the claim is that a
        // REAL session, denied, is indistinguishable from a missing one.
        var (host, handler, sessionId) = await StartWithSessionAsync();
        await using var _ = host;

        handler.OnSession = static request => request.Access == SessionAccess.Read
            ? RunAuthorizationResult.Deny("no")
            : RunAuthorizationResult.Allow();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{sessionId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var deniedProblem = await TraconTestHost.ReadJsonAsync(response);

        using var missing = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/does-not-exist-at-all", UriKind.Relative));
        var missingProblem = await TraconTestHost.ReadJsonAsync(missing);

        // The denied-but-real session and the genuinely-missing session
        // produce the IDENTICAL body - no side channel reveals which is which.
        deniedProblem.GetProperty("title").GetString().ShouldBe(missingProblem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Denied_session_delete_returns_404()
    {
        var (host, handler, sessionId) = await StartWithSessionAsync();
        await using var _ = host;

        handler.OnSession = static request => request.Access == SessionAccess.Delete
            ? RunAuthorizationResult.Deny("no")
            : RunAuthorizationResult.Allow();

        using var response = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/sessions/{sessionId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Denied_session_branch_returns_404()
    {
        var (host, handler, sessionId) = await StartWithSessionAsync();
        await using var _ = host;

        handler.OnSession = static request => request.Access == SessionAccess.Branch
            ? RunAuthorizationResult.Deny("no")
            : RunAuthorizationResult.Allow();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/sessions/{sessionId}/branch", UriKind.Relative),
            new SessionBranchRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Case: every run-starting surface is covered, not just the agent endpoint ---

    [Fact]
    public async Task Workflow_run_is_covered()
    {
        var handler = new ConfigurableRunAuthorizationHandler { OnRun = static _ => RunAuthorizationResult.Deny("no") };

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddAgent(TestData.Definition("writer"))
                .AddAgent(TestData.Definition("editor"))
                .UseWorkflows(),
            configureServices: services => Register(services, handler));

        using (var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/chain",
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = ["writer", "editor"] }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/chain/run", new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        handler.RunRequests.ShouldHaveSingleItem().AgentName.ShouldBe("chain");
    }

    [Fact]
    public async Task Inbound_trigger_is_covered()
    {
        const string secretKey = "Tracon:TriggerSecrets:RunAuthTest";
        const string secret = "whsec_test";
        const string body = """{"event":{"text":"hello"}}""";

        var handler = new ConfigurableRunAuthorizationHandler { OnRun = static _ => RunAuthorizationResult.Deny("no") };

        await using var host = await TraconTestHost.StartAsync(configureServices: services =>
        {
            Register(services, handler);
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection([new(secretKey, secret)])
                    .Build());
        });

        using (var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/triggers/hook",
            new { targetKind = "agent", targetName = "kod-agent", signingSecretConfigurationName = secretKey }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var timestamp = DateTimeOffset.UtcNow;
        var signature = WebhookSigner.Sign(body, timestamp, secret);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/tracon/api/triggers/default/hook")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSigner.SignatureHeader, signature);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        handler.RunRequests.ShouldHaveSingleItem().AgentName.ShouldBe("kod-agent");
    }

    [Fact]
    public async Task OpenAI_compatible_endpoint_is_covered()
    {
        var handler = new ConfigurableRunAuthorizationHandler { OnRun = static _ => RunAuthorizationResult.Deny("no") };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = "kod-agent", input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        handler.RunRequests.ShouldHaveSingleItem().AgentName.ShouldBe("kod-agent");
    }

    // --- Case: the ambient tenant reaches the handler, not a hardcoded default ---

    [Fact]
    public async Task Handler_receives_the_ambient_tenant()
    {
        var handler = new ConfigurableRunAuthorizationHandler();

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // "default" is SingleTenantContext's own default (K1), read through
        // ITenantContext at the moment the gate builds the request - not a
        // literal the gate itself invented.
        handler.RunRequests.ShouldHaveSingleItem().TenantId.ShouldBe("default");
    }

    // --- Case 10: the resolved user id reaches a tool through AgentRunScope ---

    [Fact]
    public async Task Allowed_user_id_reaches_the_tool_via_scope()
    {
        string? seenByTool = null;

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("scope-model").CallsTool("whoami").EchoesLastToolResult())
                .AddTool((Func<string>)(() =>
                {
                    seenByTool = TraconRunContext.Current?.UserId;

                    return seenByTool ?? "none";
                }), name: "whoami")
                .AddAgent(new AgentDefinition
                {
                    Name = "scope-agent",
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = "scope-model", Model = "m" },
                    ToolNames = ["whoami"],
                }),
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new FixedAttribution("ada")));
            });

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/scope-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        seenByTool.ShouldBe("ada");
    }

    // --- Concurrency: many runs against the same handler instance ---

    [Fact]
    public async Task Concurrent_runs_are_each_authorized_independently()
    {
        var handler = AllowOnly("a");
        await using var host = await StartAsync(handler, attribution: "a");

        var responses = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" })));

        try
        {
            responses.ShouldAllBe(static r => r.StatusCode == HttpStatusCode.OK);
            handler.RunRequests.Count.ShouldBe(16);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Starts a host with an allow-everything handler, opens one real session
    /// through it, then hands back the SAME handler instance so the caller
    /// can flip its decision for the case under test.
    /// </summary>
    private static async Task<(TraconTestHost Host, ConfigurableRunAuthorizationHandler Handler, string SessionId)> StartWithSessionAsync()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        var host = await StartAsync(handler);
        const string sessionId = "session-under-test";

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = sessionId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (host, handler, sessionId);
    }

    private static ConfigurableRunAuthorizationHandler AllowOnly(string userId)
        => new()
        {
            OnRun = request => string.Equals(request.UserId, userId, StringComparison.Ordinal)
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny($"'{request.UserId}' is not '{userId}'."),
        };

    private static Task<TraconTestHost> StartAsync(
        ConfigurableRunAuthorizationHandler? handler,
        string? attribution = null,
        Action<TraconEndpointOptions>? configureEndpoints = null)
        => TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Register(services, handler);

                if (attribution is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new FixedAttribution(attribution)));
                }
            },
            configureEndpoints: configureEndpoints);

    private static void Register(IServiceCollection services, ConfigurableRunAuthorizationHandler? handler)
    {
        if (handler is not null)
        {
            // Registered BEFORE Tracon's own TryAdd, the way a consumer
            // binding its own policy would (K4: the consumer's registration wins).
            services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler));
        }
    }

    /// <summary>The fixed detail a 403 carries when the handler threw instead of deciding.</summary>
    private const string FailedCheckDetail = "The run authorization check failed. Retry the request.";

    /// <summary>The Error lines the authorization gates wrote, in order.</summary>
    private static List<string> GateErrors(TraconTestHost host)
        => host.Logs.Entries
            .Where(static line => line.StartsWith("Error Tracon.RunAuthorization ", StringComparison.Ordinal))
            .ToList();

    private sealed class FixedAttribution(string userId) : IRunAttributionContext
    {
        public string? UserId => userId;

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class ThrowingAttribution : IRunAttributionContext
    {
        public string? UserId => throw new InvalidOperationException("the identity pipeline is down");

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class TimingOutAttribution : IRunAttributionContext
    {
        public string? UserId => throw new TaskCanceledException("the identity service timed out");

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>
    /// A configurable <see cref="IRunAuthorizationHandler"/> test double: every
    /// call is recorded (for coverage/order/tenant assertions), and the
    /// decision is delegated to an injected callback that defaults to Allow.
    /// </summary>
    private sealed class ConfigurableRunAuthorizationHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();

        public Func<RunAuthorizationRequest, RunAuthorizationResult>? OnRun { get; set; }

        public Func<SessionAuthorizationRequest, RunAuthorizationResult>? OnSession { get; set; }

        public List<RunAuthorizationRequest> RunRequests { get; } = [];

        public List<SessionAuthorizationRequest> SessionRequests { get; } = [];

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                RunRequests.Add(request);
            }

            return ValueTask.FromResult(OnRun?.Invoke(request) ?? RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                SessionRequests.Add(request);
            }

            return ValueTask.FromResult(OnSession?.Invoke(request) ?? RunAuthorizationResult.Allow());
        }
    }
}
