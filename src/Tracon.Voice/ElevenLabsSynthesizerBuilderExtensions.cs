using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Adds a second ElevenLabs synthesizer, with a concurrency limit of its own, to the Tracon chain.</summary>
public static class ElevenLabsSynthesizerBuilderExtensions
{
    /// <summary>Registers a named synthesizer that is a second built-in ElevenLabs client.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="name">
    /// The registration name, as <see cref="TraconBuilderExtensions.AddSpeechSynthesizer(ITraconBuilder, string, Func{IServiceProvider, ISpeechSynthesizer})"/>
    /// takes it: 1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c> or <c>-</c>.
    /// </param>
    /// <param name="configure">Changes to the options of <c>UseVoice</c> that this synthesizer has its own values for.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid speech provider name.</exception>
    /// <exception cref="InvalidOperationException">A synthesizer with the same name is already registered.</exception>
    /// <remarks>
    /// <para>
    /// The built-in client holds one concurrency limit (<see cref="VoiceOptions.MaxConcurrentRequests"/>) for synthesis, dialogue and
    /// transcription together. A host that voices a batch (a podcast, an audio book) and holds live conversations in one process
    /// lets the batch take the slots a conversation's turn waits for. A second client has a pool of its own: the batch calls it by
    /// name (<c>ISpeechProviderResolver.GetSynthesizerAsync("batch")</c>), and a conversation keeps the default.
    /// </para>
    /// <para>
    /// The second client starts from a copy of the options <c>UseVoice</c> was given (the key, the address, the model, the voice)
    /// and <paramref name="configure"/> changes what it should differ in, usually <see cref="VoiceOptions.MaxConcurrentRequests"/>.
    /// It is the same provider to everything that decides by provider: the tenant's egress policy and its own key apply to the
    /// name <c>elevenlabs</c>, and the price is the same. Call <c>UseVoice</c> as well; without it there are no options to copy,
    /// and the first call for the name says so.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseVoice(builder.Configuration.GetSection(VoiceOptions.SectionName))
    ///        .AddElevenLabsSynthesizer("batch", options =&gt; options.MaxConcurrentRequests = 4);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddElevenLabsSynthesizer(
        this ITraconBuilder builder,
        string name,
        Action<VoiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddSpeechSynthesizer(name, provider =>
        {
            if (provider.GetService<VoiceProviderBinding>() is null)
            {
                throw new TraconException(
                    $"The ElevenLabs synthesizer '{name}' copies the options of UseVoice, and UseVoice was not called. Call UseVoice() as well.");
            }

            var options = provider.GetRequiredService<IOptions<VoiceOptions>>().Value.Copy();

            configure?.Invoke(options);

            // The copy is held to the same rules as the options of UseVoice: a limit of zero would otherwise fail on the first call.
            var verdict = new VoiceOptionsValidator(new VoiceProviderBinding(customSynthesizer: false, customTranscriber: false), provider.GetService<IConfiguration>())
                .Validate(Options.DefaultName, options);

            if (verdict.Failed)
            {
                throw new TraconException($"The ElevenLabs synthesizer '{name}' has invalid options: {string.Join(" ", verdict.Failures ?? [])}");
            }

            return ElevenLabsClientFactory.Create(options, provider.GetService<IConfiguration>(), provider.GetService<ILoggerFactory>());
        });
    }
}
