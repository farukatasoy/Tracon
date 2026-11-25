using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Tests.Common;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A host on a real Kestrel socket that serves the voice conversation endpoint, with a clock the test moves.
/// </summary>
/// <remarks>
/// <para>
/// The in-process test server's socket and Kestrel's differ where it matters for a close: cancelling a pending receive aborts
/// the real socket and sends a reset, which the test server's socket never does. A close, an end reason and a limit that fires
/// are therefore measured here.
/// </para>
/// <para>
/// Time is the clock's: the idle limit, the duration limit and the wait for the client's close are advanced, never waited for.
/// </para>
/// </remarks>
internal sealed class KestrelVoiceHost : IAsyncDisposable
{
    /// <summary>The agent every connection talks to; it lists the tool that ends the conversation.</summary>
    public const string Agent = "voice-agent";

    private const string Provider = "voice-script";

    private readonly WebApplication _app;

    private KestrelVoiceHost(WebApplication app, Uri address, ManualClock clock, StubVoiceProvider voice)
    {
        _app = app;
        Address = address;
        Clock = clock;
        Voice = voice;
    }

    /// <summary>Gets the address the host listens on.</summary>
    public Uri Address { get; }

    /// <summary>Gets the clock the host's limits run on.</summary>
    public ManualClock Clock { get; }

    /// <summary>Gets the speech provider the conversation uses.</summary>
    public StubVoiceProvider Voice { get; }

    /// <summary>Gets the application's services.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Gets the connection limiter of the conversation endpoint.</summary>
    public VoiceConnectionLimiter Limiter => _app.Services.GetRequiredService<VoiceConversationDriver>().Limiter;

    /// <summary>Starts a host.</summary>
    /// <param name="configure">Changes the conversation options.</param>
    /// <param name="resolve">The host's connection resolver; <see langword="null"/> for none.</param>
    /// <param name="agentEnds">Whether the model calls the tool that ends the conversation on every turn.</param>
    /// <returns>The running host.</returns>
    public static async Task<KestrelVoiceHost> StartAsync(
        Action<VoiceConversationOptions>? configure = null,
        Func<VoiceConnectionRequest, CancellationToken, ValueTask<VoiceConnectionSettings?>>? resolve = null,
        bool agentEnds = false)
    {
        var voice = new StubVoiceProvider();
        var clock = new ManualClock();

        var model = new FakeModelProvider(Provider).WithModel(new ModelDescriptor { Name = "voice-1" });

        _ = agentEnds
            ? model.CallsTool(EndConversationTool.ToolName, new { }).EchoesLastToolResult()
            : model.RespondsWith("an answer");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<ISpeechTranscriber>(voice);
        builder.Services.AddSingleton<ISpeechSynthesizer>(voice);

        builder.Services.AddTracon()
            .AddModelProvider(model)
            .AddAgent(new AgentDefinition
            {
                Name = Agent,
                Description = "Talks to the customer.",
                Instructions = "Reply briefly.",
                Model = new ModelBinding { Provider = Provider, Model = "voice-1" },
                ToolNames = [EndConversationTool.ToolName],
                Origin = AgentDefinitionOrigin.Code,
            })
            .UseVoiceConversation(options =>
            {
                options.ResolveConnection = resolve;
                configure?.Invoke(options);
            });

        var app = builder.Build();
        app.MapTracon("/tracon");

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return new KestrelVoiceHost(app, new Uri(address), clock, voice);
    }

    /// <summary>Opens a conversation socket and starts the conversation.</summary>
    /// <param name="sessionId">The session the conversation runs in.</param>
    /// <returns>The socket and a client that reads its frames; the conversation is ready.</returns>
    public async Task<(ClientWebSocket Socket, VoiceConversationClient Client)> ConnectAsync(string sessionId = "session-1")
    {
        var (socket, client) = await OpenAsync(sessionId);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        return (socket, client);
    }

    /// <summary>Opens a conversation socket without starting the conversation.</summary>
    /// <param name="sessionId">The session the conversation runs in.</param>
    /// <returns>The socket and a client that reads its frames.</returns>
    public async Task<(ClientWebSocket Socket, VoiceConversationClient Client)> OpenAsync(string sessionId = "session-1")
    {
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(VoiceConversationProtocol.SubProtocol);

        var address = new UriBuilder(Address)
        {
            Scheme = "ws",
            Path = $"/tracon/api/voice/sessions/{sessionId}/stream",
        };

        await socket.ConnectAsync(address.Uri, TestContext.Current.CancellationToken);

        return (socket, new VoiceConversationClient(socket));
    }

    /// <summary>Speaks one utterance: audio, then the commit.</summary>
    /// <param name="client">The conversation client.</param>
    /// <returns>The completion task.</returns>
    public static async Task SpeakAsync(VoiceConversationClient client)
    {
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });
    }

    /// <summary>Waits until the conversation record is written and returns the only one.</summary>
    /// <returns>The record.</returns>
    public async Task<VoiceSessionRecord> WaitForRecordAsync()
    {
        var store = Services.GetRequiredService<IVoiceSessionStore>();

        var records = await WaitUntil.ValueAsync(
            async () => await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken),
            static found => found.Count > 0,
            "the conversation record to be written");

        return records.Single();
    }

    /// <summary>Waits until the clock holds a timer due exactly after <paramref name="due"/>: the wait the test is about has begun.</summary>
    /// <param name="due">The wait of the conversation; a value no other timer of the host has.</param>
    /// <returns>The completion task.</returns>
    public Task WaitForTimerAsync(TimeSpan due)
        => WaitUntil.TrueAsync(() => Clock.Pending.Contains(due), "the wait of the conversation to begin");

    /// <summary>Stops the host the way a deployment does: <see cref="IHost.StopAsync"/> first, then the disposal.</summary>
    /// <returns>The completion task.</returns>
    public Task StopAsync() => _app.StopAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
