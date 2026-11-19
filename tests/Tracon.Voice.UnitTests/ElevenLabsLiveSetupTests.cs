using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The built-in voice client sends the key and the address that are current when a request is made, taken together, when the
/// options name the configuration keys they live under.
/// </summary>
public sealed class ElevenLabsLiveSetupTests
{
    private const string KeyName = "Platform:Voice:Key";
    private const string EndpointName = "Platform:Voice:Endpoint";

    private static readonly byte[] Mp3Bytes = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public async Task A_request_carries_the_key_and_the_address_read_for_it_and_never_the_old_key_with_the_new_address()
    {
        var current = new ElevenLabsSpeechClient.ElevenLabsTarget("key-one-0001", new Uri("https://one.example/"));
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = new ElevenLabsSpeechClient(
            new VoiceOptions { DefaultVoiceId = "voice-1" },
            new HttpClient(handler),
            () => current);

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken);

        current = new ElevenLabsSpeechClient.ElevenLabsTarget("key-two-0002", new Uri("https://two.example/"));

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hello again" }, TestContext.Current.CancellationToken);

        handler.Requests.Select(static request => (request.Uri.Host, request.Headers["xi-api-key"])).ShouldBe(
            [("one.example", "key-one-0001"), ("two.example", "key-two-0002")]);
    }

    [Fact]
    public void The_registration_builds_a_client_that_reads_the_configuration_keys_when_a_request_is_made()
    {
        var configuration = Configuration((KeyName, "key-one-0001"), (EndpointName, "https://one.example/"));

        using var provider = Build(
            configuration,
            static options =>
            {
                options.ApiKeyConfigurationName = KeyName;
                options.EndpointConfigurationName = EndpointName;
            });

        var client = provider.GetRequiredService<ElevenLabsSpeechClient>();

        client.BuildUri("v1/voices").Host.ShouldBe("one.example");

        configuration[EndpointName] = "https://two.example/";

        client.BuildUri("v1/voices").Host.ShouldBe("two.example");
    }

    [Fact]
    public void A_client_whose_options_name_nothing_keeps_the_options_it_was_given()
    {
        var configuration = Configuration((KeyName, "ignored-0001"));

        using var provider = Build(configuration, static options => options.ApiKey = "start-key-0001");

        provider.GetRequiredService<ElevenLabsSpeechClient>().BuildUri("v1/voices").Host.ShouldBe("api.elevenlabs.io");
    }

    [Fact]
    public void The_options_validator_accepts_a_name_that_holds_the_key_and_refuses_one_that_holds_nothing()
    {
        var binding = new VoiceProviderBinding(customSynthesizer: false, customTranscriber: false);
        var options = new VoiceOptions { ApiKeyConfigurationName = KeyName };

        new VoiceOptionsValidator(binding, Configuration((KeyName, "key-one-0001"))).Validate(name: null, options).Succeeded.ShouldBeTrue();

        var refused = new VoiceOptionsValidator(binding, Configuration()).Validate(name: null, options);

        refused.Failed.ShouldBeTrue();
    }

    [Fact]
    public void A_name_of_blanks_is_refused()
    {
        var validator = new VoiceOptionsValidator(new VoiceProviderBinding(customSynthesizer: false, customTranscriber: false), Configuration());

        validator.Validate(name: null, new VoiceOptions { ApiKey = "key-one-0001", ApiKeyConfigurationName = "  " }).Failed.ShouldBeTrue();
        validator.Validate(name: null, new VoiceOptions { ApiKey = "key-one-0001", EndpointConfigurationName = "  " }).Failed.ShouldBeTrue();
    }

    private static IConfiguration Configuration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static ServiceProvider Build(IConfiguration configuration, Action<VoiceOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddSingleton(configuration);
        services.AddTracon().UseVoice(configure);

        return services.BuildServiceProvider();
    }
}
