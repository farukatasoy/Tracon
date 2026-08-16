using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>Settings validation runs at application STARTUP; errors surface early.</summary>
public sealed class VoiceOptionsValidatorTests
{
    [Fact]
    public void Valid_settings_are_accepted()
    {
        Validate(new VoiceOptions { ApiKey = "k", DefaultVoiceId = "v" }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Keyless_setup_names_the_user_secrets_command()
    {
        var result = Validate(new VoiceOptions());

        result.Failed.ShouldBeTrue();
        string.Join('\n', result.Failures ?? []).ShouldContain("user-secrets");
    }

    [Fact]
    public void Unstorable_output_format_is_REJECTED()
    {
        // 🚨 Raw PCM carries no file header; the attachment store validates the
        // type from the magic byte and rejects such content. The error must
        // surface at application startup, not at run time.
        foreach (var format in (string[])["pcm_16000", "PCM_24000", "ulaw_8000", "alaw_8000"])
        {
            var result = Validate(new VoiceOptions { ApiKey = "k", OutputFormat = format });

            result.Failed.ShouldBeTrue($"'{format}' was accepted even though it cannot be stored.");
            string.Join('\n', result.Failures ?? []).ShouldContain("magic byte");
        }
    }

    [Fact]
    public void Container_formats_are_accepted()
    {
        foreach (var format in (string[])["mp3_44100_128", "opus_48000_128", "wav_44100"])
        {
            VoiceOptionsValidator.IsStorableFormat(format).ShouldBeTrue(format);
        }
    }

    [Fact]
    public void Unrecognized_provider_says_to_register_your_own_implementation()
    {
        var result = Validate(new VoiceOptions { ApiKey = "k", Provider = "no-such-provider" });

        result.Failed.ShouldBeTrue();

        var text = string.Join('\n', result.Failures ?? []);
        text.ShouldContain("elevenlabs");
        text.ShouldContain("ISpeechSynthesizer");
    }

    [Fact]
    public void Zero_or_negative_limits_are_rejected()
    {
        Validate(new VoiceOptions { ApiKey = "k", MaxCharactersPerRequest = 0 }).Failed.ShouldBeTrue();
        Validate(new VoiceOptions { ApiKey = "k", MaxConcurrentRequests = 0 }).Failed.ShouldBeTrue();
        Validate(new VoiceOptions { ApiKey = "k", Timeout = TimeSpan.Zero }).Failed.ShouldBeTrue();
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(VoiceOptions options)
        => new VoiceOptionsValidator().Validate(name: null, options);
}
