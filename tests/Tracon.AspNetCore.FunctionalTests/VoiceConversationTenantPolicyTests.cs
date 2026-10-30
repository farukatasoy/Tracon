using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The conversation socket applies the tenant's speech provider checks BEFORE the
/// handshake completes and before a connection slot is taken.
/// </summary>
/// <remarks>
/// <para>
/// The conversation driver took its transcriber and synthesizer at setup and
/// sent every tenant's audio to them. The check now runs between the session gates
/// (a refusal must not confirm another tenant's session) and the slot. A refusal that
/// came after the upgrade would close a socket the client already holds, and a
/// refusal that came after the slot would hold it until the socket closed.
/// </para>
/// <para>
/// A refused handshake surfaces in <c>TestServer</c> as an exception whose message
/// carries the status; the body is not readable there (see the voice memory notes).
/// </para>
/// </remarks>
public sealed class VoiceConversationTenantPolicyTests
{
    private const string Agent = "code-agent";
    private const string Provider = "test-voice";

    [Fact]
    public async Task A_forbidden_speech_provider_refuses_the_handshake_with_403_and_holds_no_slot()
    {
        var voice = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(voice);

        await SavePolicyAsync(host, "echo");

        var refused = await Should.ThrowAsync<InvalidOperationException>(async () => await ConnectAsync(host));

        refused.Message.ShouldContain("403");
        voice.Calls.ShouldBe(0);

        // The limit is one connection per tenant. Had the refused attempt kept its
        // slot, this connection would be refused with 429.
        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .DeleteAsync("default", TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        (await client.WaitForAsync("ready")).Text("agent").ShouldBe(Agent);
    }

    [Fact]
    public async Task A_tenant_binding_the_provider_cannot_use_refuses_the_handshake()
    {
        var voice = new CountingSpeechProvider(Provider, supportsTenantCredentials: false);
        await using var host = await StartAsync(
            voice,
            services => services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection([new("Tracon:ProviderKeys:TestVoice", "tenant-voice-value")])
                .Build()));

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = Provider,
                ApiKeyConfigurationName = "Tracon:ProviderKeys:TestVoice",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        var refused = await Should.ThrowAsync<InvalidOperationException>(async () => await ConnectAsync(host));

        refused.Message.ShouldContain("403");
        voice.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_allowed_tenant_still_converses_and_the_audio_reaches_the_provider()
    {
        var voice = new CountingSpeechProvider(Provider);
        await using var host = await StartAsync(voice);

        await SavePolicyAsync(host, "echo", Provider);

        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });
        await client.WaitForAsync("done");

        voice.Received.Select(static call => call.Text).ShouldContain("transcribe", StringComparer.Ordinal);
    }

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(
        CountingSpeechProvider voice,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseVoiceConversation(static options =>
                {
                    options.OutputMediaType = "audio/mpeg";
                    options.MaxConcurrentConnectionsPerTenant = 1;
                }),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
                configureServices?.Invoke(services);
            });

    private static async Task<VoiceConversationClient> ConnectAsync(TraconTestHost host)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        var socket = await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }
}
