using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The provider-hosted live voice surface, end to end against a real local server
/// that speaks the measured GPT-Live protocol.
/// </summary>
/// <remarks>
/// 🚨 <c>TestServer</c> cannot stand in for the provider here: the live provider
/// opens an OUTGOING <c>ClientWebSocket</c> and <c>TestServer</c> fakes only the
/// incoming side. <see cref="FakeGptLiveServer"/> is therefore a real
/// <c>WebApplication</c> on a loopback port, and these tests exercise the whole path
/// — HTTP gate, provider call, attach, pump, delegation, run, record.
/// </remarks>
public sealed class LiveVoiceTests
{
    private const string Agent = "code-agent";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_session_is_created_and_the_sdp_answer_comes_back()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("voiceSessionId").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("sdp").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("model").GetString().ShouldBe("gpt-live-1");
        body.GetProperty("persistTranscript").GetBoolean().ShouldBeTrue();

        provider.CreateCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_api_key_reaches_the_provider_and_never_the_caller()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The whole point of relaying the SDP through the server: the browser
        // never sees the key, and the provider does.
        provider.CreateAuthorizationHeaders.ShouldHaveSingleItem().ShouldBe("Bearer test-openai-key");
        raw.ShouldNotContain("test-openai-key", Case.Sensitive);
    }

    [Fact]
    public async Task The_sideband_attaches_with_the_same_key()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        provider.AttachAuthorizationHeaders
            .ShouldContain(header => string.Equals(header, "Bearer test-openai-key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_create_body_carries_the_offer_the_model_and_the_delegation_mode()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var sent = JsonDocument.Parse(provider.CreateBodies.ShouldHaveSingleItem());

        sent.RootElement.GetProperty("transport").GetProperty("type").GetString().ShouldBe("webrtc");
        sent.RootElement.GetProperty("transport").GetProperty("sdp").GetString().ShouldBe(Sdp);
        sent.RootElement.GetProperty("session").GetProperty("model").GetString().ShouldBe("gpt-live-1");
        sent.RootElement.GetProperty("session").GetProperty("delegation").GetProperty("type").GetString()
            .ShouldBe("client");
    }

    [Fact]
    public async Task A_delegation_becomes_a_real_run_and_its_answer_is_spoken_back()
    {
        // This is the phase's whole reward: a spoken request becomes an ordinary
        // Tracon run, with the same recording chain as POST /api/agents/{name}/run.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await WaitForAsync(() => provider.AppendsOf("session.commentary.append").Count > 0);

        var spoken = provider.AppendsOf("session.commentary.append");
        spoken.ShouldNotBeEmpty();

        // The echo agent replies with the prompt, so the transcript has to be inside it.
        string.Join(' ', spoken).ShouldContain("Look up order 442");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var recorded = await runs.QueryRunsAsync(
            new RunQuery { Take = 10 },
            TestContext.Current.CancellationToken);

        recorded.ShouldNotBeEmpty();
        recorded[0].AgentName.ShouldBe(Agent);
    }

    [Fact]
    public async Task An_empty_cut_opens_NO_run_and_says_so_on_the_thinking_channel()
    {
        // 🚨 A run with no input still costs the consumer money and answers
        // nothing. The delegation is acknowledged instead.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        await provider.SendDelegationAsync("item_empty", 1000);

        await WaitForAsync(() => provider.AppendsOf("session.thinking.append").Count > 0);

        provider.AppendsOf("session.commentary.append").ShouldBeEmpty();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var recorded = await runs.QueryRunsAsync(
            new RunQuery { Take = 10 },
            TestContext.Current.CancellationToken);

        recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_session_record_carries_the_provider_the_model_and_the_duration_the_provider_reported()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider, configurePricing: true);

        using var response = await CreateAsync(host, "session-1");
        var created = await TraconTestHost.ReadJsonAsync(response);
        var voiceSessionId = created.GetProperty("voiceSessionId").GetGuid();

        await provider.WaitForAttachAsync(Patience);
        await provider.SendUsageAsync(30m);

        await WaitForAsync(async () =>
        {
            using var status = await host.Client.GetAsync(
                $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
                TestContext.Current.CancellationToken);

            if (status.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            var body = await TraconTestHost.ReadJsonAsync(status);

            return body.TryGetProperty("liveSeconds", out var seconds)
                && seconds.ValueKind == JsonValueKind.Number;
        });

        using var close = await host.Client.DeleteAsync(
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
            TestContext.Current.CancellationToken);

        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var records = await host.Services
            .GetRequiredService<IVoiceSessionStore>()
            .QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken);

        var record = records.ShouldHaveSingleItem();

        record.Provider.ShouldBe("openai");
        record.Model.ShouldBe("gpt-live-1");
        record.LiveSeconds.ShouldBe(30m);
        record.Cost.ShouldNotBeNull();
        record.Cost.DurationCost.ShouldBe(0.30m);
        record.Cost.Currency.ShouldBe("USD");
        record.EndReason.ShouldBe(VoiceSessionEndReason.Client);
    }

    [Fact]
    public async Task With_no_price_configured_the_cost_is_null_NOT_zero()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider, configurePricing: false);

        using var response = await CreateAsync(host, "session-1");
        var created = await TraconTestHost.ReadJsonAsync(response);
        var voiceSessionId = created.GetProperty("voiceSessionId").GetGuid();

        await provider.WaitForAttachAsync(Patience);
        await provider.SendUsageAsync(30m);

        // 🚨 Wait for the duration to actually arrive. Closing immediately would
        // leave LiveSeconds null, and a null cost would then prove nothing about
        // pricing — the test would pass with the pricing code deleted.
        await WaitForAsync(async () =>
        {
            using var status = await host.Client.GetAsync(
                $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
                TestContext.Current.CancellationToken);

            if (status.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            var body = await TraconTestHost.ReadJsonAsync(status);

            return body.TryGetProperty("liveSeconds", out var seconds)
                && seconds.ValueKind == JsonValueKind.Number;
        });

        using var close = await host.Client.DeleteAsync(
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
            TestContext.Current.CancellationToken);

        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var records = await host.Services
            .GetRequiredService<IVoiceSessionStore>()
            .QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken);

        var record = records.ShouldHaveSingleItem();

        // The duration WAS measured — so a null cost means "nobody priced this",
        // not "there was nothing to price".
        record.LiveSeconds.ShouldBe(30m);
        record.Cost.ShouldBeNull();
    }

    [Fact]
    public async Task A_dead_sideband_still_writes_the_record()
    {
        // 🚨 The record is written on EVERY path. A session that produced cost
        // and left no trace is the worst outcome available here. Which END REASON
        // each close produces is asserted in LiveVoiceLifecycleTests, which covers
        // all three: abandoned, provider-closed, and a socket that simply died.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(provider);

        using var response = await CreateAsync(host, "session-1");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);
        await provider.KillSidebandAsync();

        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        await WaitForAsync(async () =>
        {
            var records = await store.QueryAsync(
                "default",
                new VoiceSessionQuery(),
                TestContext.Current.CancellationToken);

            return records.Count == 1 && records[0].EndedAt is not null;
        });

        (await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_store_that_throws_does_not_break_the_session()
    {
        // Observability never breaks functionality: the write error is logged and
        // the conversation carries on.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(
            provider,
            configureServices: services => services.AddSingleton<IVoiceSessionStore, ThrowingVoiceSessionStore>());

        using var response = await CreateAsync(host, "session-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);
        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await WaitForAsync(() => provider.AppendsOf("session.commentary.append").Count > 0);

        provider.AppendsOf("session.commentary.append").ShouldNotBeEmpty();
    }

    // --- Helpers ---

    private static Task<HttpResponseMessage> CreateAsync(
        TraconTestHost host,
        string sessionId,
        string agent = Agent)
        => host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = sessionId, Agent = agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition never became true.");
    }

    private static Task WaitForAsync(Func<bool> condition)
        => WaitForAsync(() => Task.FromResult(condition()));

    internal static Task<TraconTestHost> StartAsync(
        FakeGptLiveServer provider,
        bool configurePricing = false,
        Action<IServiceCollection>? configureServices = null,
        Action<VoiceLiveOptions>? configureLive = null,
        bool allowPrivateNetworkTargets = true,
        bool multiTenant = false)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                if (multiTenant)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                builder
                    .AddAgent(TestData.Definition(Agent))
                    .UseOpenAI(options =>
                    {
                        options.ApiKey = "test-openai-key";
                        options.Endpoint = provider.BaseAddress;
                    })
                    .UseOpenAILive(options => options.Endpoint = provider.BaseAddress)
                    .UseLiveVoice(options =>
                    {
                        options.PersistTranscript = true;
                        configureLive?.Invoke(options);
                    });

                if (configurePricing)
                {
                    builder.Services.Configure<TraconOptions>(options =>
                    {
                        options.Pricing.Currency = "USD";
                        options.Pricing.Voice["openai"] =
                            new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["gpt-live-1"] = new() { PerMinute = 0.60m },
                            };
                    });
                }
            },
            configureServices: services =>
            {
                // The fake provider is on 127.0.0.1, which the outbound address policy
                // refuses by default. LiveVoiceEgressTests is where that refusal is
                // asserted; every other test here is about what happens once the
                // address is allowed.
                services.Configure<TraconEgressOptions>(
                    options => options.AllowPrivateNetworkTargets = allowPrivateNetworkTargets);

                configureServices?.Invoke(services);
            });

    private sealed class ThrowingVoiceSessionStore : IVoiceSessionStore
    {
        public ValueTask SaveAsync(VoiceSessionRecord record, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the voice session store is down");

        public ValueTask<IReadOnlyList<VoiceSessionRecord>> QueryAsync(
            string tenantId,
            VoiceSessionQuery query,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceSessionRecord>>([]);
    }
}
