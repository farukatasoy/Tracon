using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a live voice session and the shutdown of all of them do when the store the
/// transcript is written to fails.
/// </summary>
/// <remarks>
/// <para>
/// A session's close writes its history first and its record last, and the record is
/// the only trace a session that produced cost leaves. A history store that fails
/// with an exception the history writer does not name (an I/O or provider exception)
/// used to escape the close before the record was written, and a shutdown that closes
/// the sessions one by one stopped at the first one that threw.
/// </para>
/// </remarks>
public sealed class LiveVoiceShutdownFaultTests
{
    private const string Agent = "code-agent";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n";

    private static readonly TimeSpan Patience = LiveVoiceTests.Patience;

    [Fact]
    public async Task A_history_store_that_fails_does_not_keep_the_record_of_the_session_from_being_written()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        var plan = new FaultPlan { Fail = SessionWrites(onlyFirst: false) };
        await using var host = await StartAsync(provider, plan);

        var voiceSessionId = await OpenAsync(host, provider, "voice-close-fault");
        await ActivateAsync(host, provider, voiceSessionId);

        using var close = await host.Client.DeleteAsync(
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
            TestContext.Current.CancellationToken);

        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var records = await RecordsAsync(host);
        records.ShouldHaveSingleItem().EndReason.ShouldNotBeNull("the record is written on every path");
        plan.Calls.Any(static method => method is nameof(ISessionStore.SaveAsync)
                or nameof(ISessionStore.TryCreateAsync)
                or nameof(ISessionStore.TryUpdateAsync))
            .ShouldBeTrue("the fault must have been reached");
    }

    [Fact]
    public async Task A_session_whose_close_throws_does_not_stop_the_shutdown_of_the_sessions_after_it()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();

        // The first history write fails, whichever session reaches it first. The registry holds its
        // sessions in a hash table, so the order the shutdown closes them in is not the order they opened.
        var plan = new FaultPlan { Fail = SessionWrites(onlyFirst: true) };
        await using var host = await StartAsync(provider, plan);

        var first = await OpenAsync(host, provider, "voice-shutdown-a");
        await ActivateAsync(host, provider, first);
        var second = await OpenAsync(host, provider, "voice-shutdown-b");
        await ActivateAsync(host, provider, second);

        var shutdown = new LiveVoiceShutdownService(host.Services.GetRequiredService<LiveVoiceSessionRegistry>());

        // The shutdown reports the failure or swallows it; what it may not do is leave a session unrecorded.
        try
        {
            await shutdown.StopAsync(TestContext.Current.CancellationToken);
        }
        catch (IOException)
        {
            // The unfixed registry lets the exception of the first close out.
        }

        var records = await RecordsAsync(host);

        records.Count.ShouldBe(2);
        records.ShouldAllBe(
            static record => record.EndReason != null,
            "every session that was open at the shutdown has its record written");
        plan.Calls.Any(static method => method is nameof(ISessionStore.SaveAsync)
                or nameof(ISessionStore.TryCreateAsync)
                or nameof(ISessionStore.TryUpdateAsync))
            .ShouldBeTrue("the fault must have been reached");
    }

    /// <summary>Builds a rule that fails the writes of the session store: every one, or only the first.</summary>
    private static Func<FaultCall, Exception?> SessionWrites(bool onlyFirst)
    {
        var writes = 0;

        return call =>
        {
            if (call.Method is not (nameof(ISessionStore.SaveAsync)
                or nameof(ISessionStore.TryCreateAsync)
                or nameof(ISessionStore.TryUpdateAsync)))
            {
                return null;
            }

            return !onlyFirst || Interlocked.Increment(ref writes) == 1
                ? new IOException("the session store is unreachable")
                : null;
        };
    }

    private static Task<TraconTestHost> StartAsync(FakeGptLiveServer provider, FaultPlan plan)
        => LiveVoiceTests.StartAsync(
            provider,
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<ISessionStore>(
                _ => FaultProxy<ISessionStore>.Create(new InMemorySessionStore(), plan))),
            configureLive: options => options.PersistTranscript = true);

    private static async Task<Guid> OpenAsync(TraconTestHost host, FakeGptLiveServer provider, string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = sessionId, Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("voiceSessionId").GetGuid();
    }

    /// <summary>
    /// Sends a transcript until the session shows it. The fake provider talks to the socket attached last,
    /// so a transcript sent before the session attached lands on the previous one; sending again is harmless.
    /// </summary>
    private static Task ActivateAsync(TraconTestHost host, FakeGptLiveServer provider, Guid voiceSessionId)
        => WaitUntil.TrueAsync(
            async () =>
            {
                await provider.SendInputTranscriptAsync(" Where is order 442", 600, 1200);

                using var status = await host.Client.GetAsync(
                    $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
                    TestContext.Current.CancellationToken);

                return string.Equals(
                    (await TraconTestHost.ReadJsonAsync(status)).GetProperty("state").GetString(),
                    "active",
                    StringComparison.Ordinal);
            },
            $"live voice session {voiceSessionId} to carry a transcript",
            Patience);

    private static async Task<IReadOnlyList<VoiceSessionRecord>> RecordsAsync(TraconTestHost host)
    {
        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        return await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken);
    }
}
