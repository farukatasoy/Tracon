// POST /tracon/api/voice/live/sessions with a provider that accepts the create call and refuses the sideband handshake
// (FakeGptLiveServer.RefuseAttach) answers 502 with a problem+json body, as the create-call refusal does. It answered 500
// with no Content-Type and an empty body: ClientWebSocket.ConnectAsync throws WebSocketException for a refused handshake
// and the endpoint's catch list did not name it, so it reached the server's last-resort handler. Only a real Kestrel
// socket shows it (the in-process test server hands the exception to the client), which is why this test starts one.
// The aftermath (slot returned, record closed with Error, host still serving) is pinned by LiveVoiceSidebandRefusalTests.
using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

public sealed class LiveVoiceSidebandRefusalStatusTests
{
    [Fact]
    public async Task A_refused_sideband_is_answered_with_a_502_problem()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        provider.RefuseAttach = true;

        await using var app = await LiveVoiceSidebandRefusalTests.StartKestrelAsync(provider);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        using var reply = await LiveVoiceSidebandRefusalTests.CreateAsync(client, "refused");
        var body = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        reply.StatusCode.ShouldBe(HttpStatusCode.BadGateway, $"the real server answered {(int)reply.StatusCode} with body '{body}'");
        reply.Content.Headers.ContentType.ShouldNotBeNull();
        reply.Content.Headers.ContentType.MediaType.ShouldBe("application/problem+json");
        body.ShouldContain("Live voice session could not be created");
    }
}
