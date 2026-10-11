using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A live voice session obeys the tenant's provider policy exactly as a model call
/// does: the egress policy, then the tenant's own credential (F-306).
/// </summary>
/// <remarks>
/// <para>
/// The session is bound to a tenant, but the live provider used the process-wide
/// OpenAI key and nothing read the tenant's policy: a tenant whose egress policy
/// excluded <c>openai</c> still had its audio sent to OpenAI, and a tenant with its
/// own key was billed to the installation's.
/// </para>
/// <para>
/// Measured against <see cref="FakeGptLiveServer"/>, a real loopback server, so the
/// assertions read what actually reached the provider: no call at all when the
/// policy refuses, and the tenant's key on BOTH the create call and the attach
/// handshake when the tenant has its own.
/// </para>
/// </remarks>
public sealed class LiveVoiceTenantPolicyTests
{
    private const string Agent = "code-agent";
    private const string KeyName = "Tracon:ProviderKeys:OpenAI";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n";

    [Fact]
    public async Task A_tenant_whose_egress_policy_excludes_the_live_provider_cannot_open_a_session()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", ["anthropic"], TestContext.Current.CancellationToken);

        using var response = await CreateAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("'openai'");
        provider.CreateCallCount.ShouldBe(0, "the tenant's audio must not reach a provider its policy forbids");
    }

    [Fact]
    public async Task A_tenant_whose_egress_policy_allows_the_live_provider_opens_a_session()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        // "echo" is the delegated agent's own model provider: the policy governs
        // that model too, and a policy that forbids it refuses the agent instead.
        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", ["openai", "echo"], TestContext.Current.CancellationToken);

        using var response = await CreateAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.CreateCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_tenant_with_its_own_openai_binding_opens_and_attaches_the_session_with_its_own_key()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "tenant-openai-key")]).Build()));

        await SaveBindingAsync(host);

        using var response = await CreateAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await provider.WaitForAttachAsync(LiveVoiceTests.Patience);

        provider.CreateAuthorizationHeaders.ShouldHaveSingleItem().ShouldBe("Bearer tenant-openai-key");
        provider.AttachAuthorizationHeaders.ShouldAllBe(header => header == "Bearer tenant-openai-key");
        provider.AttachAuthorizationHeaders.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_binding_whose_key_has_no_value_refuses_the_session_instead_of_using_the_configured_key()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        await SaveBindingAsync(host);

        using var response = await CreateAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("does NOT fall back to the global key");
        provider.CreateCallCount.ShouldBe(0, "the installation's key would have billed the wrong account");
    }

    [Fact]
    public async Task A_binding_with_an_endpoint_override_is_refused_rather_than_sent_elsewhere()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "tenant-openai-key")]).Build()));

        await SaveBindingAsync(host, endpoint: "https://proxy.example.test/v1/");

        using var response = await CreateAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("endpoint override");
        provider.CreateCallCount.ShouldBe(0);

        // Refused before the slot and the record: no session row was written.
        (await host.Services.GetRequiredService<IVoiceSessionStore>()
            .QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_that_cannot_use_a_tenant_credential_is_refused_for_a_tenant_with_its_own_binding()
    {
        // A third-party provider written before the tenant credential existed does
        // not set SupportsTenantCredentials. It would silently open the session on
        // its own configured key - the wrong account - so the session is refused.
        var stub = new CountingLiveVoiceProvider();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseLiveVoice(),
            configureServices: services =>
            {
                services.AddSingleton<ILiveVoiceProvider>(stub);
                services.AddSingleton<IConfiguration>(
                    new ConfigurationBuilder().AddInMemoryCollection([new(StubKeyName, "tenant-stub-key")]).Build());
            });

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = "stub",
                ApiKeyConfigurationName = StubKeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        using var response = await CreateAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        stub.CreateCalls.ShouldBe(0);
    }

    private const string StubKeyName = "Tracon:ProviderKeys:Stub";

    private static Task SaveBindingAsync(TraconTestHost host, string? endpoint = null)
        => host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = "openai",
                ApiKeyConfigurationName = KeyName,
                Endpoint = endpoint,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken).AsTask();

    /// <summary>A provider that predates the tenant credential: it does not override the defaults.</summary>
    private sealed class CountingLiveVoiceProvider : ILiveVoiceProvider
    {
        private int _createCalls;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public string ProviderName => "stub";

        public string ModelId => "stub-live";

        public int MaxAppendCharacters => 1000;

        public ValueTask<LiveVoiceSessionHandle> CreateSessionAsync(
            LiveVoiceCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCalls);

            return ValueTask.FromResult(new LiveVoiceSessionHandle { ProviderSessionId = "stub-session", SdpAnswer = "v=0\r\n" });
        }

        public ValueTask<ILiveVoiceSideband> AttachAsync(
            string providerSessionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The stub provider does not attach.");
    }

    private static Task<HttpResponseMessage> CreateAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "tenant-policy", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);
}
