using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a live conversation leaves behind, and whether the caller was told.
/// </summary>
/// <remarks>
/// <para>
/// The audio of a live session never reaches Tracon, so nothing can store it.
/// The <strong>text</strong> is a different matter: the transcript is what a
/// delegation is cut from, and by default it is also written to the agent session's
/// durable history. That is a real privacy consequence and it is handled two ways —
/// it can be switched off, and either way the value is reported to the caller when
/// the session is created.
/// </para>
/// <para>
/// 🚨 Switching it off must not switch delegation off with it. The ledger still
/// exists in memory for the session's lifetime; only the durable write is skipped.
/// </para>
/// </remarks>
public sealed class LiveVoicePrivacyTests
{
    private const string Agent = "code-agent";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task With_PersistTranscript_on_the_answer_says_so()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.PersistTranscript = true);

        using var response = await CreateAsync(host);
        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("persistTranscript").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task With_PersistTranscript_off_the_answer_says_so_too()
    {
        // No recording happens silently, and no absence of recording is implied
        // silently either: the caller is told which of the two it is.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.PersistTranscript = false);

        using var response = await CreateAsync(host);
        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("persistTranscript").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task With_PersistTranscript_off_delegation_STILL_works()
    {
        // 🚨 The switch is about durability, not capability. A delegation still
        // sees the conversation, because the ledger it is cut from lives in memory.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.PersistTranscript = false);

        using var response = await CreateAsync(host);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await WaitForAsync(() => provider.AppendsOf("session.commentary.append").Count > 0);

        provider.AppendsOf("session.commentary.append").ShouldNotBeEmpty();

        // And it really was a run: the delegation went through the ordinary chain.
        var runs = host.Services.GetRequiredService<IRunStore>();

        (await runs.QueryRunsAsync(new RunQuery { Take = 10 }, TestContext.Current.CancellationToken))
            .ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_sdp_offer_is_never_written_to_the_session_record()
    {
        // An SDP carries the caller's network addresses. It is relayed to the
        // provider and kept nowhere else.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        using var response = await CreateAsync(host);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var records = await host.Services
            .GetRequiredService<IVoiceSessionStore>()
            .QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken);

        var record = records.ShouldHaveSingleItem();

        // Nothing on the record is a place an SDP could hide.
        record.Provider.ShouldBe("openai");
        record.Model.ShouldBe("gpt-live-1");

        foreach (var field in new[] { record.Provider, record.Model, record.CreatedBy, record.SessionId })
        {
            (field is not null && field.Contains("v=0", StringComparison.Ordinal)).ShouldBeFalse();
        }
    }

    private static Task<HttpResponseMessage> CreateAsync(TraconTestHost host)
        => host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "session-1", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

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
}
