using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The outbound address policy applies to <strong>both</strong> halves of the live
/// voice provider: the REST call that creates a session and the WebSocket that
/// attaches to it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 This is the repository's first OUTGOING WebSocket, and it is the reason
/// these tests exist. <c>EgressSocketGuard</c> is built around
/// <c>SocketsHttpHandler.ConnectCallback</c>; <c>ClientWebSocket</c> has no such
/// hook, so a socket opened without an explicit check would leave the policy
/// silently skipped — a server-side request forgery hole that looks like working
/// code.
/// </para>
/// <para>
/// The two halves are asserted SEPARATELY on purpose. The REST call inherits the
/// policy through the handler, so proving it says nothing about the socket; the
/// socket calls the guard by hand, and only a test that reaches
/// <c>AttachAsync</c> itself can show that the call is still there.
/// </para>
/// </remarks>
public sealed class LiveVoiceEgressTests
{
    private const string Agent = "code-agent";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n";

    [Fact]
    public async Task The_session_creating_REST_call_goes_through_the_egress_guard()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider, allowPrivateNetworkTargets: false);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "session-1", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

        // The loopback provider is a private-network target, and the policy refuses
        // it. The caller gets a gateway error, not a session.
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        // 🚨 The strongest part of the assertion: the provider was never reached.
        provider.CreateCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_attach_socket_goes_through_the_egress_guard()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider, allowPrivateNetworkTargets: false);

        // AttachAsync is exercised directly: with the policy strict, the REST call
        // above never gets far enough to produce a session id, so the socket half
        // would otherwise never be reached by a test at all.
        var live = host.Services.GetRequiredService<ILiveVoiceProvider>();

        var attach = async () => await live.AttachAsync("live_whatever", TestContext.Current.CancellationToken);

        var error = await attach.ShouldThrowAsync<TraconException>();

        error.Message.ShouldContain(
            nameof(TraconEgressOptions.AllowPrivateNetworkTargets),
            Case.Sensitive);

        // The handshake never happened.
        provider.AttachAuthorizationHeaders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Both_halves_connect_once_the_policy_allows_the_target()
    {
        // The mirror image: the same two calls succeed when the address is allowed,
        // so the refusals above are the policy speaking and not a broken client.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider, allowPrivateNetworkTargets: true);

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "session-1", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(TimeSpan.FromSeconds(10));

        provider.CreateCallCount.ShouldBe(1);
        provider.AttachAuthorizationHeaders.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_transport_failure_keeps_the_provider_address_out_of_the_answer()
    {
        // The two tests above take the branch where the refusal is OURS: the policy
        // rejects the target and its verdict arrives as a TraconException inside the
        // transport failure, so reporting that inner message is right. This test takes
        // the OTHER branch — the policy allows the target and the transport itself
        // fails, so the HttpRequestException carries no verdict of ours. .NET puts the
        // target address into the message of a refused connection, and that address is
        // the deployment's outbound endpoint, which a caller must not learn.
        await using var provider = await FakeGptLiveServer.StartAsync();
        var address = provider.BaseAddress;

        await using var host = await LiveVoiceTests.StartAsync(provider, allowPrivateNetworkTargets: true);

        // Close the provider so the next call is refused at the transport layer.
        await provider.DisposeAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = "session-1", Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain(address.Port.ToString(CultureInfo.InvariantCulture));
        body.ShouldNotContain(address.Host, Case.Sensitive);
    }
}
