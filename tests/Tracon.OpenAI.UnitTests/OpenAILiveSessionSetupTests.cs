using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracon.OpenAI.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// A live voice session reads the key and the address once, before it is created, and uses that pair for the creation and
/// for the attachment.
/// </summary>
/// <remarks>
/// The server answers 400 on purpose: the proof is the request head it read, and a refusal by the provider ends the call
/// with a failure that carries none of the provider's body.
/// </remarks>
public sealed class OpenAILiveSessionSetupTests
{
    private const string KeyName = "Platform:OpenAI:Key";
    private const string LiveEndpointName = "Platform:OpenAI:LiveEndpoint";

    [Fact]
    public async Task A_provider_whose_key_and_address_are_fixed_reads_the_options_as_it_always_did()
    {
        await using var provider = Services(Configuration(), static options => options.ApiKey = TestData.ApiKey);
        using var live = NewProvider(provider, liveEndpointName: null, endpoint: null);

        live.SetupCredential().ShouldBeNull();
    }

    [Fact]
    public async Task A_session_is_created_with_the_pair_read_before_it_and_a_rotation_in_between_changes_nothing()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", "{}");
        var configuration = Configuration((KeyName, "key-one-0001"), (LiveEndpointName, server.BaseAddress.ToString()));
        await using var provider = Services(configuration, static options => options.ApiKeyConfigurationName = KeyName);
        using var live = NewProvider(provider, LiveEndpointName, endpoint: null, configuration);

        // The launcher reads the pair once...
        var credential = live.SetupCredential().ShouldNotBeNull();

        // ...and the key rotates before the call to the service is made.
        configuration[KeyName] = "key-two-0002";

        await Should.ThrowAsync<TraconException>(
            async () => await live.CreateSessionAsync(
                new LiveVoiceCreateRequest { SdpOffer = "v=0", Credential = credential },
                TestContext.Current.CancellationToken));

        var head = await server.RequestHead;

        head.ShouldStartWith("POST /live/sessions HTTP/1.1");
        head.ShouldContain("Authorization: Bearer key-one-0001");
        credential.Endpoint.ShouldBe(server.BaseAddress.ToString());
    }

    [Fact]
    public async Task The_next_session_reads_the_pair_again()
    {
        var configuration = Configuration((KeyName, "key-one-0001"));
        await using var provider = Services(configuration, static options => options.ApiKeyConfigurationName = KeyName);
        using var live = NewProvider(provider, liveEndpointName: null, endpoint: new Uri("https://live.example/v1/"), configuration);

        live.SetupCredential().ShouldNotBeNull().ApiKey.ShouldBe("key-one-0001");

        configuration[KeyName] = "key-two-0002";

        live.SetupCredential().ShouldNotBeNull().ApiKey.ShouldBe("key-two-0002");
    }

    [Fact]
    public async Task A_live_address_alone_is_enough_and_the_key_is_the_one_the_options_hold()
    {
        await using var server = StubHttpServer.Respond(400, "Bad Request", "{}");
        var configuration = Configuration((LiveEndpointName, server.BaseAddress.ToString()));
        await using var provider = Services(configuration, static options => options.ApiKey = TestData.ApiKey + " ");
        using var live = NewProvider(provider, LiveEndpointName, endpoint: null, configuration);

        var credential = live.SetupCredential().ShouldNotBeNull();

        await Should.ThrowAsync<TraconException>(
            async () => await live.CreateSessionAsync(
                new LiveVoiceCreateRequest { SdpOffer = "v=0", Credential = credential },
                TestContext.Current.CancellationToken));

        (await server.RequestHead).ShouldContain($"Authorization: Bearer {TestData.ApiKey}\r\n");
    }

    private static OpenAILiveProvider NewProvider(
        ServiceProvider provider,
        string? liveEndpointName,
        Uri? endpoint,
        IConfiguration? configuration = null)
    {
        var liveOptions = Substitute.For<IOptionsMonitor<OpenAILiveOptions>>();
        liveOptions.CurrentValue.Returns(new OpenAILiveOptions { Endpoint = endpoint, EndpointConfigurationName = liveEndpointName });

        return new OpenAILiveProvider(
            liveOptions,
            provider.GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>(),
            new EgressSocketGuard(static () => new EgressAddressPolicy(AllowPrivateNetworkTargets: true, AllowLoopback: true)),
            NullLoggerFactory.Instance,
            provider.GetRequiredService<OpenAINamedChatClientFactoryCache>(),
            configuration);
    }

    private static IConfiguration Configuration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static ServiceProvider Services(IConfiguration configuration, Action<OpenAIProviderOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddTracon().UseOpenAI(configure);

        return services.BuildServiceProvider();
    }
}
