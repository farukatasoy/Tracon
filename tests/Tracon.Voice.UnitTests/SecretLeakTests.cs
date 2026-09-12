using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// Verifies that the API key does not leak into any output path.
/// </summary>
/// <remarks>
/// The pattern is the same across all four provider packages. A secret leak
/// causes no error at compile time or run time; only a test like this catches it.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string ApiKey = "FAKE-VOICE-KEY-xyz789";

    [Fact]
    public void Options_class_does_NOT_define_its_own_ToString_method()
    {
        // 🚨 K-035. If this were a `record`, the compiler-generated ToString would
        // write out all properties, and a single LogDebug call would expose the key.
        var declaringType = typeof(VoiceOptions).GetMethod(nameof(ToString))!.DeclaringType;

        declaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public void Options_text_representation_does_NOT_carry_the_key()
    {
        var options = new VoiceOptions { ApiKey = ApiKey };

        // The call is made via reflection: writing `options.ToString()` directly
        // yields MA0150 ("the default object.ToString will be used") — which is
        // exactly what this test wants to PROVE. Moving the call to reflection
        // instead of suppressing the analyzer preserves both the rule and the
        // run-time behavior.
        var text = (string?)typeof(VoiceOptions).GetMethod(nameof(ToString))!.Invoke(options, null);

        text.ShouldNotBeNull().ShouldNotContain(ApiKey);
    }

    [Fact]
    public void Validation_message_does_NOT_carry_the_key_or_the_address()
    {
        var validator = new VoiceOptionsValidator();

        var result = validator.Validate(
            name: null,
            new VoiceOptions
            {
                ApiKey = ApiKey,
                Endpoint = new Uri("https://secret-server.example/"),
                Provider = "unknown-provider",
                OutputFormat = "pcm_16000",
            });

        result.Failed.ShouldBeTrue();

        var text = string.Join('\n', result.Failures ?? []);
        text.ShouldNotContain(ApiKey);
        text.ShouldNotContain("secret-server");
    }

    [Fact]
    public void Key_is_not_visible_when_the_health_output_is_serialized()
    {
        var health = new VoiceHealth
        {
            ProviderName = VoiceProviderNames.ElevenLabs,
            IsHealthy = false,
            Latency = TimeSpan.FromMilliseconds(12),
            CheckedAt = DateTimeOffset.UnixEpoch,
            Detail = "Connection error (ConnectionError).",
        };

        JsonSerializer.Serialize(health).ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task No_line_written_to_the_log_carries_the_key()
    {
        using var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger("test");

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($$"""{"detail":"bad key {{ApiKey}}"}"""),
        });

        var options = new VoiceOptions { ApiKey = ApiKey, DefaultVoiceId = "voice-1" };
        using var client = new ElevenLabsSpeechClient(options, new HttpClient(handler));

        try
        {
            _ = await client.SynthesizeAsync(
                new SpeechRequest { Text = "hello" },
                TestContext.Current.CancellationToken);
        }
        catch (TraconException exception)
        {
            // What the application would do: log the error.
            logger.LogError(exception, "Speech synthesis failed.");
        }

        loggerProvider.AllText.ShouldNotContain(ApiKey);
    }

    [Fact]
    public async Task Voice_list_never_carries_preview_url_into_the_public_model()
    {
        // 🚨 preview_url is a DELIBERATE omission (playing it would connect the
        // browser to the provider's address, VoiceDescriptor's own remarks).
        // ElevenLabsVoice has no property for it, so the field can never reach
        // VoiceDescriptor or its Attributes no matter what the response contains.
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """
            {"voices":[
              {
                "voice_id": "a",
                "name": "Amy",
                "category": "premade",
                "preview_url": "https://storage.example/secret-path.mp3",
                "labels": {"gender": "female"},
                "verified_languages": [{"language": "en", "model_id": "m1"}]
              }
            ]}
            """));

        var options = new VoiceOptions { ApiKey = ApiKey, DefaultVoiceId = "voice-1" };
        using var client = new ElevenLabsSpeechClient(options, new HttpClient(handler));

        var voices = await client.ListVoicesAsync(TestContext.Current.CancellationToken);

        var voice = voices.ShouldHaveSingleItem();
        var serialized = JsonSerializer.Serialize(voice);

        serialized.ShouldNotContain("preview_url");
        serialized.ShouldNotContain("storage.example");
        voice.Attributes[VoiceAttributeNames.Gender].ShouldBe("female");
    }

    [Fact]
    public async Task Key_appears_only_in_the_REQUEST_header_not_in_the_body()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary([0x49, 0x44, 0x33, 0x04]));
        var options = new VoiceOptions { ApiKey = ApiKey, DefaultVoiceId = "voice-1" };
        using var client = new ElevenLabsSpeechClient(options, new HttpClient(handler));

        _ = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hello" },
            TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();

        request.Headers["xi-api-key"].ShouldBe(ApiKey);
        (request.Body ?? string.Empty).ShouldNotContain(ApiKey);
        request.Uri.ToString().ShouldNotContain(ApiKey);
    }
}
