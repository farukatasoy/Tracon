using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The key the OpenAI live voice provider is called with is sent to that provider (on the create call and on the sideband
/// attach) and appears nowhere else: not in a response, a voice session record, a log record in any of its parts, a span,
/// a metric tag or a byte of the database.
/// </summary>
/// <remarks>
/// <para>
/// The provider is the live voice fake the other live voice tests use: a server that creates a session, accepts the
/// sideband WebSocket and sends the events a session produces, and that keeps the <c>Authorization</c> header of both
/// calls. The scenarios are the ones the key meets: a session that opens, speaks and closes, a create the provider
/// refuses with a body that quotes the key, and an attach the provider refuses.
/// </para>
/// </remarks>
public sealed class LiveVoiceKeyCanaryTests
{
    private const string Agent = "code-agent";
    private const string Stem = "canary-live-openai-key";
    private const string Key = Stem + "-b2d4e86031a95c7f";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n";

    [Fact]
    public async Task The_live_voice_key_reaches_the_provider_and_no_other_output()
    {
        using var database = new TempSqliteDatabase("live-voice-key-canary");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureServices: services => services.AddLogging(logging => logging.AddProvider(structured)),
            apiKey: Key,
            configureStore: builder => builder.UseSqlite(database.ConnectionString));

        var drive = new CanaryDrive(host);

        // A session that opens, hears and speaks, and closes.
        using var opened = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/live/sessions", UriKind.Relative),
            new LiveVoiceSessionCreateRequest { SessionId = "session-ok", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

        opened.StatusCode.ShouldBe(HttpStatusCode.OK);

        var created = await TraconTestHost.ReadJsonAsync(opened);
        var voiceSessionId = created.GetProperty("voiceSessionId").GetGuid();

        drive.Keep(created.ToString());

        await provider.WaitForAttachAsync(LiveVoiceTests.Patience);
        await provider.SendInputTranscriptAsync(" Where is my order", 100, 900);
        await provider.SendOutputTranscriptAsync("It is on its way.", 1000, 1900);
        await provider.SendUsageAsync(2.5m);

        await drive.SendAsync(HttpMethod.Get, $"/tracon/api/voice/live/sessions/{voiceSessionId:D}");
        (await drive.SendAsync(HttpMethod.Delete, $"/tracon/api/voice/live/sessions/{voiceSessionId:D}")).ShouldBe(HttpStatusCode.NoContent);

        // The provider refuses the create and quotes the key it was sent.
        provider.CreateStatusCode = (int)HttpStatusCode.Unauthorized;
        provider.CreateRefusalBody = "{\"error\":{\"message\":\"Incorrect API key provided: " + Key + ". Find your key at the provider's console.\",\"code\":\"invalid_api_key\"}}";
        await CreateAsync(drive, "session-refused");

        // The provider accepts the create and refuses the sideband.
        provider.CreateStatusCode = (int)HttpStatusCode.Created;
        provider.RefuseAttach = true;
        await CreateAsync(drive, "session-no-sideband");

        (await drive.ReadEverythingAsync()).ShouldBeGreaterThan(40);

        // Both calls carried the key, so an empty search is not for a session that never reached the provider.
        provider.CreateAuthorizationHeaders.ShouldContain(static header => header != null && header.Contains(Key, StringComparison.Ordinal), "the provider was never sent the key on the create call");
        provider.AttachAuthorizationHeaders.ShouldContain(static header => header != null && header.Contains(Key, StringComparison.Ordinal), "the provider was never sent the key on the sideband attach");

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, "session-ok").ShouldNotBeEmpty("the voice session is not in the database, so the scan reads the wrong place");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        var hits = SecretHits.Find(outputs, [new("live voice key", Stem)]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, Stem))
        {
            hits.Add($"the live voice key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    private static Task<HttpStatusCode> CreateAsync(CanaryDrive drive, string sessionId)
        => drive.SendAsync(
            HttpMethod.Post,
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = sessionId, Agent = Agent, Sdp = Sdp });
}
