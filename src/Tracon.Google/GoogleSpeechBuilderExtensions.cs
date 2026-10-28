using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>Adds Google Gemini speech to the Tracon chain.</summary>
public static class GoogleSpeechBuilderExtensions
{
    /// <summary>Registers Gemini text-to-speech as a named Tracon speech synthesizer that also voices a dialogue.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The speech settings. The model is required.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="configure"/> sets no model, a name that is not a valid speech provider name, a character
    /// limit that is not greater than zero, or a <see cref="GoogleSpeechOptions.DialogueSpeakerMode"/> that is
    /// not defined.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call <see cref="GoogleProviderExtensions.UseGoogle(ITraconBuilder, string, Action{GoogleProviderOptions}?)"/>
    /// first. This call shares the authenticated Google client that <c>UseGoogle</c> registers and adds no package
    /// and no second connection pool.
    /// </para>
    /// <para>
    /// The synthesizer is a named speech provider: every call passes the tenant's egress policy against the
    /// provider name <c>google</c>, the name the chat provider uses, so one rule covers chat, images and speech.
    /// A tenant that has its own Google key is served with that key: the request goes to the same client the
    /// tenant's chat requests use.
    /// </para>
    /// <para>
    /// <strong>One voice.</strong> <c>POST /api/voice/speak</c> with <c>"provider": "gemini"</c> answers a WAV file.
    /// <see cref="SpeechRequest.OutputFormat"/> takes <c>wav</c> (the default) or <c>pcm</c>; another format is
    /// refused before the call. A streamed answer is raw 16-bit PCM at the model's sample rate.
    /// </para>
    /// <para>
    /// <strong>A dialogue.</strong> The same instance voices a dialogue of exactly two voices
    /// (<see cref="ISpeechProviderResolver.GetDialogueSynthesizerAsync"/>). The request names its speakers in the
    /// format the model takes; see <see cref="GoogleSpeechOptions.DialogueSpeakerMode"/>. Gemini reports no timing,
    /// so a request for timestamps is refused.
    /// </para>
    /// <para>
    /// Billing is an estimate. Gemini bills speech by tokens, and the answer carries no character count, so Tracon
    /// records the length of the text and labels it as estimated.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseGoogle(builder.Configuration.GetSection(GoogleProviderOptions.SectionName))
    ///        .UseGoogleSpeech(options =&gt;
    ///        {
    ///            options.Model = "gemini-3.8-flash-tts";
    ///            options.Voices.Add(new VoiceDescriptor { VoiceId = "Kore", Name = "Kore" });
    ///            options.Voices.Add(new VoiceDescriptor { VoiceId = "Puck", Name = "Puck" });
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseGoogleSpeech(
        this ITraconBuilder builder,
        Action<GoogleSpeechOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // Read now, not at startup: the registration depends on what is set.
        var options = new GoogleSpeechOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ArgumentException(
                $"Set {nameof(GoogleSpeechOptions.Model)}: Tracon carries no Gemini model name, so there is nothing to register without one.",
                nameof(configure));
        }

        if (options.MaxCharactersPerRequest <= 0)
        {
            throw new ArgumentException(
                $"{nameof(GoogleSpeechOptions.MaxCharactersPerRequest)} must be greater than zero.",
                nameof(configure));
        }

        if (options.MaxDialogueCharactersPerRequest <= 0)
        {
            throw new ArgumentException(
                $"{nameof(GoogleSpeechOptions.MaxDialogueCharactersPerRequest)} must be greater than zero.",
                nameof(configure));
        }

        if (!Enum.IsDefined(options.DialogueSpeakerMode))
        {
            throw new ArgumentException(
                $"{nameof(GoogleSpeechOptions.DialogueSpeakerMode)} is not a {nameof(GoogleDialogueSpeakerMode)} value.",
                nameof(configure));
        }

        // The name is checked by the registration below.
        var settings = new GoogleSpeechOptions
        {
            Model = options.Model,
            Name = options.Name,
            MaxCharactersPerRequest = options.MaxCharactersPerRequest,
            MaxDialogueCharactersPerRequest = options.MaxDialogueCharactersPerRequest,
            DialogueSpeakerMode = options.DialogueSpeakerMode,
            UseAsDefault = options.UseAsDefault,
        };

        foreach (var voice in options.Voices)
        {
            settings.Voices.Add(voice);
        }

        builder.AddSpeechSynthesizer(settings.Name, provider =>
        {
            var google = provider.GetRequiredService<GoogleModelProvider>();

            return new GoogleSpeechSynthesizer(
                settings,
                provider.GetRequiredService<GoogleChatClientFactory>().Client,
                credential => google.GetTenantFactory(credential).Client);
        });

        if (settings.UseAsDefault)
        {
            builder.Services.TryAddSingleton<ISpeechSynthesizer>(provider => provider.GetRequiredKeyedService<ISpeechSynthesizer>(
                SpeechProviderNames.TryNormalize(settings.Name, out var normalized)
                    ? new SpeechProviderRegistration(SpeechProviderKind.Synthesizer, normalized)
                    : throw new ArgumentException(SpeechProviderNames.InvalidNameMessage, nameof(configure))));
        }

        return builder;
    }
}
