using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The live voice provider reuses the OpenAI key set up at start, so it trims and checks that key like the chat client
/// does before it becomes the <c>Authorization</c> header of the session request.
/// </summary>
/// <remarks>
/// The server answers 400 on purpose: the proof is the request head it read, and a refusal by the provider ends the call
/// with a failure that carries no part of the provider's body.
/// </remarks>
public sealed class OpenAILiveSetupKeyTests
{
    private const string OptionName = "OpenAIProviderOptions.ApiKey";

    [Fact]
    public async Task The_session_request_carries_the_setup_key_without_the_white_space_around_it()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", "{}");
        using var provider = NewProvider(TestData.ApiKey + "\r\n", server.BaseAddress);

        await Should.ThrowAsync<TraconException>(
            async () => await provider.CreateSessionAsync(new LiveVoiceCreateRequest { SdpOffer = "v=0" }, TestContext.Current.CancellationToken));

        (await server.RequestHead).ShouldContain($"Authorization: Bearer {TestData.ApiKey}\r\n");
    }

    [Fact]
    public async Task A_setup_key_a_header_cannot_carry_stops_the_session_before_a_call_and_the_failure_does_not_repeat_it()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", "{}");
        using var provider = NewProvider("test-key-\0-0123456789", server.BaseAddress);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await provider.CreateSessionAsync(new LiveVoiceCreateRequest { SdpOffer = "v=0" }, TestContext.Current.CancellationToken));

        failure.Message.ShouldStartWith(OptionName);
        failure.Message.ShouldNotContain("test-key-", Case.Sensitive);
        failure.Message.ShouldNotContain("0123456789", Case.Sensitive);
        server.RequestHead.IsCompleted.ShouldBeFalse("a call was made with a key that cannot be sent");
    }

    private static OpenAILiveProvider NewProvider(string apiKey, Uri endpoint)
    {
        var live = Substitute.For<IOptionsMonitor<OpenAILiveOptions>>();
        live.CurrentValue.Returns(new OpenAILiveOptions { Endpoint = endpoint });

        var openAi = Substitute.For<IOptionsMonitor<OpenAIProviderOptions>>();
        openAi.CurrentValue.Returns(TestData.Options(options => options.ApiKey = apiKey));

        return new OpenAILiveProvider(
            live,
            openAi,
            new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)),
            NullLoggerFactory.Instance);
    }
}
