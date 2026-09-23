using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the real-time conversation endpoint (<c>/api/voice/sessions/{id}/stream</c>).
/// </summary>
/// <remarks>
/// The test project <strong>does NOT</strong> reference <c>Tracon.Voice</c>:
/// the conversation layer knows only the <see cref="ISpeechTranscriber"/> and
/// <see cref="ISpeechSynthesizer"/> abstractions. ElevenLabs is an
/// implementation (K-215).
/// </remarks>
public sealed class VoiceConversationTests
{
    private const string Agent = "code-agent";
    private const string StreamPath = "/tracon/api/voice/sessions/session-1/stream";

    [Fact]
    public async Task NO_endpoint_opens_when_UseVoiceConversation_was_not_called()
    {
        // 🚨 The most important test in this phase: a capability that changes
        // the hosting model must not open silently. The endpoint returns 404
        // ("no such address"), NOT 501 ("exists but disabled") — because it
        // genuinely does not exist.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri(StreamPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Returns_501_when_no_voice_provider()
    {
        // A conversation needs both transcription and synthesis; with only
        // one, the conversation would be one-directional, and that is not a
        // conversation.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.UseVoiceConversation());

        using var response = await host.Client.GetAsync(new Uri(StreamPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Non_WebSocket_request_returns_400()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri(StreamPath, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task End_to_end_single_turn_conversation()
    {
        var voice = new StubVoiceProvider { Transcript = "where is my order" };
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var ready = await client.WaitForAsync("ready");
        ready.Text("agent").ShouldBe(Agent);
        ready.Text("sessionId").ShouldBe("session-1");

        // Audio is NOT saved by default, and the interface shows this to the user.
        ready.Flag("persistAudio").ShouldBe(false);

        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();

        var transcript = await client.WaitForAsync("transcript", frames);
        transcript.Text("text").ShouldBe("where is my order");
        transcript.Flag("final").ShouldBe(true);

        var runStarted = await client.WaitForAsync("runStarted", frames);
        Guid.TryParse(runStarted.Text("runId"), out _).ShouldBeTrue();

        var done = await client.WaitForAsync("done", frames);
        done.Flag("cancelled").ShouldBe(false);

        // Caption text streamed, audio chunks arrived.
        frames.ShouldContain(static frame => frame.Type == "text");
        frames.ShouldContain(static frame => frame.Type == "audioStart");
        frames.ShouldContain(static frame => frame.IsAudio);
        frames.ShouldContain(static frame => frame.Type == "audioEnd");

        // 🚨 Raw PCM goes to transcription as WAV: headerless audio is not a
        // file on its own.
        voice.ReceivedMediaType.ShouldBe("audio/wav");
        voice.ReceivedBytes.ShouldBe(3200 + 44);
        voice.Spoken.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Each_turn_produces_a_normal_run_row()
    {
        // Voice does NOT change the run path; it only changes the input and
        // output format. Proof: the recording, span, and cost all come from
        // the existing path.
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        var runStarted = await client.WaitForAsync("runStarted");
        await client.WaitForAsync("done");

        var runId = Guid.Parse(runStarted.Text("runId")!);
        var runs = host.Services.GetRequiredService<IRunStore>();

        var record = await runs.GetRunAsync(runId, TestContext.Current.CancellationToken);

        record.ShouldNotBeNull();
        record.AgentName.ShouldBe(Agent);
        record.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Interruption_cancels_the_run_and_writes_the_partial_reply_to_history()
    {
        // 🚨 If the interrupted reply is not written, the model will NOT SEE
        // its own half-finished sentence on the next turn, and the
        // conversation breaks.
        var voice = new StubVoiceProvider { SynthesisDelay = TimeSpan.FromSeconds(5) };
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        // Synthesis started but did not finish: the exact moment of interruption.
        await client.WaitForAsync("audioStart");
        await client.SendControlAsync(new { type = "cancel" });

        var done = await client.WaitForAsync("done");
        done.Flag("cancelled").ShouldBe(true);

        using var history = await host.Client.GetAsync(
            new Uri("/tracon/api/sessions/session-1", UriKind.Relative));

        history.EnsureSuccessStatusCode();

        var text = await history.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Contains("interrupted", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
    }

    [Fact]
    public async Task Conversation_record_is_written_and_does_NOT_contain_audio()
    {
        await using var host = await StartAsync();

        await using (var client = await ConnectAsync(host))
        {
            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
            await client.WaitForAsync("ready");

            await client.SendAudioAsync(new byte[1600]);
            await client.SendControlAsync(new { type = "commit" });
            await client.WaitForAsync("done");

            await client.SendControlAsync(new { type = "stop" });
        }

        // Closing waits for the record to be written; the endpoint must be able to read it.
        var records = await WaitForRecordsAsync(host);

        records.GetArrayLength().ShouldBe(1);
        records[0].GetProperty("agentName").GetString().ShouldBe(Agent);
        records[0].GetProperty("turns").GetInt32().ShouldBe(1);
        records[0].GetProperty("sessionId").GetString().ShouldBe("session-1");
        records[0].GetProperty("endReason").GetString().ShouldBe("Client");

        // The record does NOT contain audio: none of the columns carry content.
        records[0].TryGetProperty("audio", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task When_PersistAudio_is_on_ONLY_the_agents_voice_is_saved_as_an_attachment()
    {
        // 🚨 The user's voice is never saved: audio is biometric data, and
        // the record of what was said already exists as the transcript in
        // session history. The only thing saved is the voice the agent
        // PRODUCES.
        await using var host = await StartAsync(configureConversation: static options =>
            options.PersistAudio = true);

        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        // The interface SHOWS the user that recording is happening; recording never happens silently.
        (await client.WaitForAsync("ready")).Flag("persistAudio").ShouldBe(true);

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        var done = await client.WaitForAsync("done");
        var attachmentId = done.Text("attachmentId").ShouldNotBeNull();

        using var download = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments/{attachmentId}", UriKind.Relative));

        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType?.MediaType.ShouldBe("audio/mpeg");

        // The attachment must be LINKED to the session; an orphaned
        // attachment is deleted by the retention policy (Phase 28/G1).
        using var listing = await host.Client.GetAsync(
            new Uri("/tracon/api/attachments?sessionId=session-1", UriKind.Relative));

        listing.EnsureSuccessStatusCode();

        var body = await TraconTestHost.ReadJsonAsync(listing);
        body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Audio_is_NOT_saved_by_default()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        (await client.WaitForAsync("ready")).Flag("persistAudio").ShouldBe(false);

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        (await client.WaitForAsync("done")).Text("attachmentId").ShouldBeNull();

        using var listing = await host.Client.GetAsync(
            new Uri("/tracon/api/attachments?sessionId=session-1", UriKind.Relative));

        listing.EnsureSuccessStatusCode();
        (await TraconTestHost.ReadJsonAsync(listing)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Token_is_accepted_in_the_subprotocol()
    {
        await using var host = await StartAsync(authToken: "secret-token");
        await using var client = await ConnectAsync(host, token: "secret-token");

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        (await client.WaitForAsync("ready")).Text("agent").ShouldBe(Agent);
    }

    [Fact]
    public async Task Token_in_the_QUERY_STRING_is_NOT_accepted()
    {
        // 🚨 The address is written to server logs, reverse-proxy logs, and
        // browser history; the token must not be placed there.
        await using var host = await StartAsync(authToken: "secret-token");

        var socketClient = host.CreateWebSocketClient();
        socketClient.ConfigureRequest = static request => request.Headers.Remove("Authorization");

        var connect = async () => await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream?token=secret-token"),
            TestContext.Current.CancellationToken);

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Connection_is_rejected_when_token_is_wrong()
    {
        await using var host = await StartAsync(authToken: "secret-token");

        var connect = async () => await ConnectAsync(host, token: "wrong-token");

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Another_tenants_session_CANNOT_be_accessed()
    {
        // sessionId is untrusted input.
        //
        // 🚨 The behavior CHANGED in Phase 41. Previously the store was
        // tenant-blind: the endpoint could read another tenant's session,
        // see it, and REJECT it as "belonging to someone else". This was an
        // existence oracle — a tenant could probe, via the error code, which
        // session ids existed for ANOTHER tenant. The store is now scoped to
        // the tenant (K-277): an invisible session is treated as NOT
        // EXISTING, the connection opens a fresh session in its own tenant,
        // and the other tenant's record is NEVER touched. Isolation is
        // stronger, and leaked information is reduced.
        await using var host = await StartAsync();

        var sessions = host.Services.GetRequiredService<ISessionStore>();

        await sessions.SaveAsync(
            new SessionRecord
            {
                Id = "someone-elses-session",
                AgentName = Agent,
                State = System.Text.Json.JsonDocument.Parse("""{"secret":true}""").RootElement,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                TenantId = "other-tenant",
            },
            TestContext.Current.CancellationToken);

        // The current tenant can never see that record.
        (await sessions.GetAsync("someone-elses-session", TestContext.Current.CancellationToken)).ShouldBeNull();

        var socketClient = host.CreateWebSocketClient();

        using (await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/someone-elses-session/stream"),
            TestContext.Current.CancellationToken))
        {
            // The connection is established; but the session on the other
            // end is NOT the other tenant's session.
        }

        // The other tenant's record remains intact.
        var theirs = (await sessions.QueryAsync(
            new SessionQuery { TenantId = "other-tenant" },
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        theirs.Id.ShouldBe("someone-elses-session");
        theirs.State.GetProperty("secret").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_connection_limit_is_enforced()
    {
        await using var host = await StartAsync(configureConversation: static options =>
            options.MaxConcurrentConnectionsPerTenant = 1);

        await using var first = await ConnectAsync(host);

        var connect = async () => await ConnectAsync(host);

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unknown_audio_format_is_rejected()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "mp3" });

        (await client.WaitForAsync("error")).Text("message")
            .ShouldNotBeNull()
            .ShouldContain("mp3");
    }

    [Fact]
    public async Task Nonexistent_agent_returns_an_error()
    {
        await using var host = await StartAsync();
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = "no-such-agent" });

        (await client.WaitForAsync("error")).Text("message")
            .ShouldNotBeNull()
            .ShouldContain("no-such-agent");
    }

    [Fact]
    public async Task Commit_without_audio_does_NOT_produce_a_turn()
    {
        // A short cough can also trigger the client's VAD; this is not an
        // error and must not interrupt the conversation.
        var voice = new StubVoiceProvider();
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendControlAsync(new { type = "commit" });

        // If a new turn can be started again, it has returned to listening.
        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        (await client.WaitForAsync("done")).Json.ShouldNotBeNull()
            .GetProperty("turn").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Commit_without_audio_tells_the_client_it_is_listening_again()
    {
        // 🚨 The defect this locks: the server went back to listening on its own
        // and sent NOTHING. The client had already put itself in 'thinking' the
        // moment the user pressed send, and only a server frame moves it back AND
        // restarts the recorder - so the panel hung for good and the user could
        // not speak again. Its sibling above measured the SERVER's state (a later
        // turn still works) and could never see this: the gap was the frame the
        // client never got.
        //
        // Reachable from the interface, not only in theory: pressing send within
        // the recorder's first 250 ms timeslice commits before any audio has been
        // sent. Under full-suite load that window is wide enough that the E2E
        // voice test hit it and hung for its full 30 s timeout.
        var voice = new StubVoiceProvider();
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendControlAsync(new { type = "commit" });

        // Bounded on purpose: without the frame this waits for the whole test
        // timeout and reports a cancellation instead of the missing contract.
        await client.WaitForAsync("idle")
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unintelligible_audio_tells_the_client_it_is_listening_again()
    {
        // The second case of the same class, found by scanning for it and likelier
        // than the first: the audio DID arrive, and the transcriber heard nothing
        // usable - a knock on the desk, a noisy room. The client ignores a
        // transcript frame whose text is empty, so that frame cannot be the one
        // that releases it.
        var voice = new StubVoiceProvider { Transcript = string.Empty };
        await using var host = await StartAsync(voice);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        await client.WaitForAsync("idle")
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static Task<TraconTestHost> StartAsync(
        StubVoiceProvider? voice = null,
        string? authToken = null,
        Action<VoiceConversationOptions>? configureConversation = null)
    {
        var provider = voice ?? new StubVoiceProvider();

        return TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseVoiceConversation(options =>
                {
                    options.OutputMediaType = "audio/mpeg";
                    configureConversation?.Invoke(options);
                }),
            configureEndpoints: options =>
            {
                if (authToken is { Length: > 0 })
                {
                    options.AuthToken = authToken;
                }
            },
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            });
    }

    private static async Task<VoiceConversationClient> ConnectAsync(
        TraconTestHost host,
        string? token = null,
        string sessionId = "session-1")
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        if (token is { Length: > 0 })
        {
            socketClient.SubProtocols.Add(VoiceConversationProtocol.TokenSubProtocolPrefix + token);
        }

        var socket = await socketClient.ConnectAsync(
            new Uri($"http://localhost/tracon/api/voice/sessions/{sessionId}/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }

    /// <summary>Polls the conversation list until the record is written.</summary>
    /// <remarks>
    /// The record is written when the socket closes; the client's
    /// <c>Dispose</c> does not wait for the server's closing work.
    /// </remarks>
    private static Task<System.Text.Json.JsonElement> WaitForRecordsAsync(TraconTestHost host)
        => WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync(
                    new Uri("/tracon/api/voice/sessions", UriKind.Relative));

                response.EnsureSuccessStatusCode();

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetArrayLength() > 0,
            "the conversation record to be written");
}
