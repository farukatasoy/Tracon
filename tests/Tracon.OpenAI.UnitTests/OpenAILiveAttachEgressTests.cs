using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The sideband socket opens the address the egress guard resolved and judged, once: the connection and the judgement are
/// the same resolution, so a name that answers differently the second time cannot be judged on one address and opened on
/// another.
/// </summary>
/// <remarks>
/// The endpoint name does not resolve through the operating system (<c>.invalid</c>), and the guard's resolver maps it to
/// the loopback server. A socket that resolved the name itself would never reach the server. The server answers 400, so
/// the handshake fails after the request head was read, which is all the test needs.
/// </remarks>
public sealed class OpenAILiveAttachEgressTests
{
    [Fact]
    public async Task The_attach_socket_opens_the_address_the_guard_resolved_and_asks_the_resolver_once()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", "{}");
        var resolutions = 0;

        var guard = new EgressSocketGuard(
            static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true),
            (_, _) =>
            {
                Interlocked.Increment(ref resolutions);

                return ValueTask.FromResult(new[] { IPAddress.Loopback });
            });

        using var provider = NewProvider(guard, new Uri($"http://provider.invalid:{server.BaseAddress.Port}/"));

        await Should.ThrowAsync<Exception>(
            async () => await provider.AttachAsync("live_1", TestContext.Current.CancellationToken));

        (await server.RequestHead.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
            .ShouldStartWith("GET /live/sessions/live_1/attach", Case.Sensitive);
        resolutions.ShouldBe(1, "the name was resolved once, and the connection went to that answer");
    }

    [Fact]
    public async Task A_refusal_of_the_guard_reaches_the_caller_as_the_refusal_and_not_as_a_failed_handshake()
    {
        var guard = new EgressSocketGuard(
            static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: false, AllowLoopback: false),
            static (_, _) => ValueTask.FromResult(new[] { IPAddress.Loopback }));

        using var provider = NewProvider(guard, new Uri("http://provider.invalid:9/"));

        var refusal = await Should.ThrowAsync<TraconException>(
            async () => await provider.AttachAsync("live_1", TestContext.Current.CancellationToken));

        refusal.Message.ShouldContain(nameof(TraconEgressOptions.AllowPrivateNetworkTargets), Case.Sensitive);
    }

    private static OpenAILiveProvider NewProvider(EgressSocketGuard guard, Uri endpoint)
    {
        var live = Substitute.For<IOptionsMonitor<OpenAILiveOptions>>();
        live.CurrentValue.Returns(new OpenAILiveOptions { Endpoint = endpoint });

        var openAi = Substitute.For<IOptionsMonitor<OpenAIProviderOptions>>();
        openAi.CurrentValue.Returns(TestData.Options(options => options.ApiKey = TestData.ApiKey));

        return new OpenAILiveProvider(live, openAi, guard, NullLoggerFactory.Instance);
    }
}
