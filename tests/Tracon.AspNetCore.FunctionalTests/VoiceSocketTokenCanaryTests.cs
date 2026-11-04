using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The static token that guards the voice conversation socket is accepted from a browser through a WebSocket
/// subprotocol (a browser cannot set a header on a socket) and appears nowhere else: not in the subprotocol the server
/// selects, not in a frame the server sends, not in a log record or a span.
/// </summary>
/// <remarks>
/// <para>
/// The token travels in the handshake as <c>tracon.token.&lt;token&gt;</c>, next to the real subprotocol. The server has to pick
/// the real one and never answer with the one that carries the secret: a header the server writes is a header a proxy
/// logs. The frames of a whole turn (ready, transcript, run started, text, audio markers, done) and of a refused start
/// (an unknown agent) are collected and searched.
/// </para>
/// <para>
/// The token is required, so a socket that opens proves it was read; a socket without it, and one with another value, are
/// refused and what they were told is searched too.
/// </para>
/// </remarks>
public sealed class VoiceSocketTokenCanaryTests
{
    private const string Agent = "voice-agent";
    private const string TokenStem = "canary-voice-socket-token";
    private const string Token = TokenStem + "-c7e1a9540d62b3f8";
    private const string WrongStem = "canary-voice-socket-wrong";
    private const string StreamPath = "http://localhost/tracon/api/voice/sessions/session-canary/stream";

    [Fact]
    public async Task The_token_of_a_voice_socket_is_not_selected_back_and_is_in_no_frame_and_no_other_output()
    {
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();
        var voice = new StubVoiceProvider { Transcript = "where is my order" };

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureEndpoints: static options => options.AuthToken = Token,
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
                services.AddLogging(logging => logging.AddProvider(structured));
            });

        var seen = new System.Text.StringBuilder();

        // A socket with the token opens, and the server selects the real subprotocol, not the one that carries the secret.
        using (var socket = await ConnectAsync(host, Token))
        {
            socket.SubProtocol.ShouldBe(VoiceConversationProtocol.SubProtocol);
            seen.AppendLine("selected subprotocol: " + socket.SubProtocol);

            await using var client = new VoiceConversationClient(socket);

            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
            await client.WaitForAsync("ready");
            await client.SendAudioAsync(new byte[3200]);
            await client.SendControlAsync(new { type = "commit" });

            var frames = new List<VoiceEvent>();

            await client.WaitForAsync("done", frames);

            frames.ShouldContain(static frame => frame.Type == "transcript", "the turn did not run, so the frames hold nothing to search");

            foreach (var frame in frames)
            {
                seen.AppendLine(frame.Json?.ToString() ?? "<audio>");
            }
        }

        // A start the server refuses answers with an error frame.
        using (var socket = await ConnectAsync(host, Token))
        {
            await using var client = new VoiceConversationClient(socket);

            await client.SendControlAsync(new { type = "start", agent = "no-such-agent", inputFormat = "pcm16" });

            var frames = new List<VoiceEvent>();

            await client.WaitForAsync("error", frames);

            foreach (var frame in frames)
            {
                seen.AppendLine(frame.Json?.ToString() ?? "<audio>");
            }
        }

        // Without the token, and with another value, the handshake is refused; what the refusal says is kept.
        foreach (var token in (string?[])[null, WrongStem + "-5a0e"])
        {
            var refused = await Should.ThrowAsync<Exception>(async () =>
            {
                using var socket = await ConnectAsync(host, token);
            });

            seen.AppendLine("refused: " + refused);
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["frames, selected subprotocol and refusals"] = seen.ToString(),
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        SecretHits.Find(outputs, [new("socket token", TokenStem), new("wrong socket token", WrongStem)]).ShouldBeEmpty();
    }

    private static async Task<WebSocket> ConnectAsync(TraconTestHost host, string? token)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        if (token is not null)
        {
            socketClient.SubProtocols.Add(VoiceConversationProtocol.TokenSubProtocolPrefix + token);
        }

        return await socketClient.ConnectAsync(new Uri(StreamPath), TestContext.Current.CancellationToken);
    }
}
