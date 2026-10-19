using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>Adds OpenAI speech to the Tracon chain.</summary>
public static class OpenAISpeechBuilderExtensions
{
    /// <summary>Registers OpenAI text-to-speech and speech-to-text as named Tracon speech providers.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The speech settings. At least one model is required.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="configure"/> sets neither model, or sets a name that is not a valid speech provider name,
    /// or a character limit that is not greater than zero.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call <see cref="OpenAIProviderExtensions.UseOpenAI(ITraconBuilder, string, Action{OpenAIProviderOptions}?)"/>
    /// first. This call shares the authenticated OpenAI client that <c>UseOpenAI</c>
    /// registers and does not add a package or a second connection pool.
    /// </para>
    /// <para>
    /// The providers are named speech providers: every call passes the tenant's egress policy
    /// against the provider name <c>openai</c>, the name the chat provider uses, so one rule
    /// covers chat, images and speech. A tenant that has its own OpenAI key is refused for
    /// speech: the client was built with the setup key and cannot use another.
    /// </para>
    /// <para>
    /// Billing is an estimate. The speech endpoint reports no billed characters, so Tracon records
    /// the length of the text and labels it as estimated. A transcription has no duration, so it
    /// has no cost: the client reads no duration from the answer.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!)
    ///        .UseOpenAISpeech(options =&gt;
    ///        {
    ///            options.SynthesisModel = "gpt-4o-mini-tts";
    ///            options.TranscriptionModel = "gpt-4o-mini-transcribe";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseOpenAISpeech(
        this ITraconBuilder builder,
        Action<OpenAISpeechOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        // Read now, not at startup: which providers exist depends on which models are set.
        var options = new OpenAISpeechOptions();
        configure(options);

        var synthesisModel = Model(options.SynthesisModel);
        var transcriptionModel = Model(options.TranscriptionModel);

        if (synthesisModel is null && transcriptionModel is null)
        {
            throw new ArgumentException(
                $"Set {nameof(OpenAISpeechOptions.SynthesisModel)}, {nameof(OpenAISpeechOptions.TranscriptionModel)} or both: " +
                "there is nothing to register without a model.",
                nameof(configure));
        }

        var services = builder.Services;

        if (synthesisModel is not null)
        {
            var key = new OpenAISpeechClientKey(SpeechProviderKind.Synthesizer, options.SynthesizerName);

            builder.AddSpeechSynthesizer(
                options.SynthesizerName,
                new TextToSpeechClientBinding
                {
                    ServiceKey = key,
                    // Explicit, not read from the client: the egress rule for chat, images and
                    // speech is one rule, and a client's metadata can change with its package.
                    ProviderName = OpenAIProviderNames.ChatCompletions,
                    MaxCharactersPerRequest = options.MaxCharactersPerRequest,
                    Voices = [.. options.Voices],
                });
            OpenAISpeechClientRegistration.AddTextToSpeech(services, key, synthesisModel);

            if (options.UseAsDefault)
            {
                services.TryAddSingleton<ISpeechSynthesizer>(provider => provider.GetRequiredKeyedService<ISpeechSynthesizer>(
                    NamedRegistration(SpeechProviderKind.Synthesizer, options.SynthesizerName)));
            }
        }

        if (transcriptionModel is not null)
        {
            var key = new OpenAISpeechClientKey(SpeechProviderKind.Transcriber, options.TranscriberName);

            builder.AddSpeechTranscriber(
                options.TranscriberName,
                new SpeechToTextClientBinding
                {
                    ServiceKey = key,
                    ProviderName = OpenAIProviderNames.ChatCompletions,
                });
            OpenAISpeechClientRegistration.AddSpeechToText(services, key, transcriptionModel);

            if (options.UseAsDefault)
            {
                services.TryAddSingleton<ISpeechTranscriber>(provider => provider.GetRequiredKeyedService<ISpeechTranscriber>(
                    NamedRegistration(SpeechProviderKind.Transcriber, options.TranscriberName)));
            }
        }

        return builder;
    }

    private static string? Model(string? model)
        => string.IsNullOrWhiteSpace(model) ? null : model.Trim();

    /// <summary>Builds the key the named provider is registered under; the name was checked by the registration above.</summary>
    private static SpeechProviderRegistration NamedRegistration(SpeechProviderKind kind, string name)
        => SpeechProviderNames.TryNormalize(name, out var normalized)
            ? new SpeechProviderRegistration(kind, normalized)
            : throw new ArgumentException(SpeechProviderNames.InvalidNameMessage, nameof(name));
}
