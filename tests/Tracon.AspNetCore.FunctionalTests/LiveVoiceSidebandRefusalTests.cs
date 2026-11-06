using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A live voice session whose create call the provider accepts and whose sideband it refuses fails the request, leaves
/// the host serving, gives the tenant's slot back and closes the session record with the error reason.
/// </summary>
/// <remarks>
/// <para>
/// The in-process test server hands an exception of the request to the client as an exception, where Kestrel answers
/// it with a status code; only a real socket tells which one a caller sees, so this host listens on loopback and the
/// client is a plain <see cref="HttpClient"/>.
/// </para>
/// <para>
/// A tenant may hold one open session here. Two refused attempts in a row both reaching the provider is how the test
/// sees that the first one gave its slot back.
/// </para>
/// <para>
/// The status code itself is not pinned to a value. A sideband the provider refuses reaches the endpoint as a
/// <see cref="System.Net.WebSockets.WebSocketException"/>, which the endpoint's bad-gateway answer does not name; the
/// real server answers it with <c>500</c> and an empty body (measured). The probe that asks for a <c>502</c> problem
/// lives beside this file as <c>LiveVoiceSidebandRefusalStatusTests.cs.finding</c>; this test stays green on either answer.
/// </para>
/// </remarks>
public sealed class LiveVoiceSidebandRefusalTests
{
    internal const string Agent = "code-agent";
    internal const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n";

    [Fact]
    public async Task A_refused_sideband_fails_the_request_and_gives_its_slot_back()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        provider.RefuseAttach = true;

        await using var app = await StartKestrelAsync(provider);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        foreach (var sessionId in (string[])["refused-one", "refused-two"])
        {
            using var reply = await CreateAsync(client, sessionId);

            ((int)reply.StatusCode).ShouldBeGreaterThanOrEqualTo(500, "a sideband the provider refuses is not a success and not the caller's mistake");
        }

        // Both attempts reached the provider: the first one did not hold the only slot.
        provider.CreateCallCount.ShouldBe(2);
        provider.AttachAuthorizationHeaders.Count.ShouldBe(2);

        // The host still serves.
        using (var meta = await client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            meta.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Every session the refusal opened was closed with the error reason.
        var store = app.Services.GetRequiredService<IVoiceSessionStore>();
        var records = await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken);

        records.Count.ShouldBe(2);
        records.ShouldAllBe(static record => record.EndReason == VoiceSessionEndReason.Error && record.EndedAt != null);
    }

    internal static Task<HttpResponseMessage> CreateAsync(HttpClient client, string sessionId)
        => client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/live/sessions", UriKind.Relative),
            new LiveVoiceSessionCreateRequest { SessionId = sessionId, Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

    internal static async Task<WebApplication> StartKestrelAsync(FakeGptLiveServer provider)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);

        builder.Services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);

        var tracon = builder.Services.AddTracon();
        tracon.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage());
        tracon.AddAgent(TestData.Definition(Agent));
        tracon
            .UseOpenAI(options =>
            {
                options.ApiKey = "test-openai-key";
                options.Endpoint = provider.BaseAddress;
            })
            .UseOpenAILive(options => options.Endpoint = provider.BaseAddress)
            .UseLiveVoice(static options => options.MaxConcurrentSessionsPerTenant = 1);

        var app = builder.Build();

        app.MapTracon("/tracon");
        await app.StartAsync();

        return app;
    }
}
