using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Tenancy, registration and limits on the live voice surface.
/// </summary>
/// <remarks>
/// <para>
/// Two rules carry the weight here. A caller that may not reach a session is told
/// it does not exist, in the same words an unreachable session already produces —
/// otherwise the status code becomes an oracle for which sessions exist in another
/// tenant. And the concurrency limit answers <strong>before</strong> the provider is
/// called, because a session that is created and then refused is still a billed
/// session.
/// </para>
/// <para>
/// 🚨 The tenancy test is the one that matters most. The sideband pump runs
/// outside the HTTP request that created it, so a delegated run lands in the default
/// tenant unless the pump re-establishes the ambient tenant itself. A unit test
/// cannot see that; only a run recorded under a second tenant can.
/// </para>
/// </remarks>
public sealed class LiveVoiceAuthorizationTests
{
    private const string Agent = "code-agent";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_delegation_run_is_recorded_under_the_sessions_OWN_tenant()
    {
        // 🚨 The phase's most likely silent defect. The pump is not on the request's
        // thread and inherits nothing from it; if the ambient tenant is not opened on
        // the pump itself, every delegated run lands in "default" and every test that
        // only ever uses one tenant stays green.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1", tenant: "tenant-b");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await WaitForAsync(() => provider.AppendsOf("session.commentary.append").Count > 0);

        var runs = host.Services.GetRequiredService<IRunStore>();

        using (AmbientTenantScope.Begin("tenant-b"))
        {
            var mine = await runs.QueryRunsAsync(
                new RunQuery { Take = 10 },
                TestContext.Current.CancellationToken);

            mine.ShouldNotBeEmpty();
            mine[0].TenantId.ShouldBe("tenant-b");
        }

        using (AmbientTenantScope.Begin("default"))
        {
            var elsewhere = await runs.QueryRunsAsync(
                new RunQuery { Take = 10 },
                TestContext.Current.CancellationToken);

            elsewhere.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_session_record_is_written_under_the_sessions_own_tenant()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1", tenant: "tenant-b");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        (await store.QueryAsync("tenant-b", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .TenantId.ShouldBe("tenant-b");

        (await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_tenants_session_cannot_be_closed_or_read()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var created = await CreateAsync(host, "session-1", tenant: "tenant-a");
        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        var id = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("voiceSessionId").GetGuid();

        using var close = await SendAsync(host, HttpMethod.Delete, id, tenant: "tenant-b");
        using var read = await SendAsync(host, HttpMethod.Get, id, tenant: "tenant-b");

        close.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 Byte for byte the answer a session that does not exist at all gets.
        using var missing = await SendAsync(host, HttpMethod.Get, Guid.NewGuid(), tenant: "tenant-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var denied = await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var absent = await missing.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Normalize(denied).ShouldBe(Normalize(absent));
    }

    [Fact]
    public async Task A_denied_caller_is_told_the_session_does_not_exist()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnSession = static request => request.Access == SessionAccess.Voice
                ? RunAuthorizationResult.Deny("not your conversation")
                : RunAuthorizationResult.Allow(),
        };

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(
            provider,
            services => services.TryAddSingleton<IRunAuthorizationHandler>(handler));

        using var response = await CreateAsync(host, "session-of-a");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 The provider was never called: a denial must not create a billed session.
        provider.CreateCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_session_is_still_opened_when_the_handler_allows_it()
    {
        // K-283's case on the live surface: a session that does not exist yet is
        // NOT an error — the conversation opens it. A gate that refused unknown
        // sessions would break the first conversation of every installation.
        var handler = new ConfigurableRunAuthorizationHandler();

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(
            provider,
            services => services.TryAddSingleton<IRunAuthorizationHandler>(handler));

        var sessions = host.Services.GetRequiredService<ISessionStore>();
        (await sessions.GetAsync("never-seen-before", TestContext.Current.CancellationToken)).ShouldBeNull();

        using var response = await CreateAsync(host, "never-seen-before");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The handler was still ASKED.
        handler.SessionRequests.ShouldHaveSingleItem().SessionId.ShouldBe("never-seen-before");
    }

    [Fact]
    public async Task The_concurrency_limit_answers_BEFORE_the_provider_is_called()
    {
        // 🚨 The whole reason the check sits in the launcher rather than after the
        // create call: a refused request that had already created a session would
        // bill the consumer for every rejection.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(
            provider,
            configureLive: options => options.MaxConcurrentSessionsPerTenant = 1);

        using var first = await CreateAsync(host, "session-1");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var second = await CreateAsync(host, "session-2");

        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // One session created, one refused, ONE provider call.
        provider.CreateCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_limit_is_per_tenant()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(
            provider,
            configureLive: options => options.MaxConcurrentSessionsPerTenant = 1);

        using var a = await CreateAsync(host, "session-1", tenant: "tenant-a");
        using var b = await CreateAsync(host, "session-1", tenant: "tenant-b");

        a.StatusCode.ShouldBe(HttpStatusCode.OK);
        b.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_agent_is_a_404_and_costs_nothing()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1", agent: "no-such-agent");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        provider.CreateCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_body_with_no_sdp_offer_is_refused_before_the_provider_is_called()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "session-1", Agent = Agent, Sdp = "   " },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        provider.CreateCallCount.ShouldBe(0);
    }

    // --- Helpers ---

    /// <summary>Blanks every identifier so two answers can be compared on wording alone.</summary>
    /// <param name="body">The response body.</param>
    /// <returns>The body with identifiers replaced.</returns>
    /// <remarks>
    /// The id is the ONLY thing the two answers are allowed to differ by; everything
    /// else — status, title, detail wording — has to match, or the status code becomes
    /// an oracle for which sessions exist in another tenant.
    /// </remarks>
    private static string Normalize(string body)
        => GuidPattern.Replace(body, "<id>");

    private static readonly System.Text.RegularExpressions.Regex GuidPattern = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static Task<HttpResponseMessage> CreateAsync(
        TraconTestHost host,
        string sessionId,
        string agent = Agent,
        string? tenant = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/voice/live/sessions")
        {
            Content = JsonContent.Create(
                new LiveVoiceSessionCreateRequest { SessionId = sessionId, Agent = agent, Sdp = Sdp }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> SendAsync(
        TraconTestHost host,
        HttpMethod method,
        Guid voiceSessionId,
        string tenant)
    {
        var request = new HttpRequestMessage(
            method,
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}");

        request.Headers.Add(TenantHeader, tenant);

        return host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition never became true.");
    }

    private static Task<TraconTestHost> StartAsync(
        FakeGptLiveServer provider,
        Action<IServiceCollection>? configureServices = null,
        Action<VoiceLiveOptions>? configureLive = null)
        => LiveVoiceTests.StartAsync(
            provider,
            configureServices: configureServices,
            configureLive: configureLive,
            multiTenant: true);

    /// <summary>An authorization handler whose answer the test decides.</summary>
    private sealed class ConfigurableRunAuthorizationHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();

        public Func<SessionAuthorizationRequest, RunAuthorizationResult>? OnSession { get; set; }

        public List<SessionAuthorizationRequest> SessionRequests { get; } = [];

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());

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
