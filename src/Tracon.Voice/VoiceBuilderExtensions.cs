using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Adds the voice tools to the Tracon chain.</summary>
public static class VoiceBuilderExtensions
{
    /// <summary>Registers the voice tools, reading settings from configuration.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">The <c>Tracon:Voice</c> section.</param>
    /// <param name="configure">Changes to apply after configuration binding.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">When a dependency is <see langword="null"/>.</exception>
    /// <remarks>
    /// Registers the speech tools an agent can call. The real-time
    /// conversation layer is a separate, opt-in call
    /// (<c>UseVoiceConversation()</c>).
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseVoice(builder.Configuration.GetSection("Tracon:Voice"));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseVoice(
        this ITraconBuilder builder,
        IConfiguration configurationSection,
        Action<VoiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseVoice(options =>
        {
            BindOptions(configurationSection, options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers the voice tools.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">When a dependency is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registration uses <c>TryAdd*</c>: when the consumer registered its own
    /// <see cref="ISpeechSynthesizer"/> or <see cref="ISpeechTranscriber"/>
    /// implementation BEFORE this call, theirs is preserved. When both are the
    /// consumer's, the ElevenLabs settings (provider name, API key, address) are
    /// not required.
    /// </para>
    /// <para>
    /// The settings are validated when the application starts; an invalid value
    /// stops the host before the first request.
    /// </para>
    /// <para>
    /// Three tools are registered: <c>speak</c>, <c>transcribe</c>, <c>list_voices</c>.
    /// tools are defined IN CODE; the UI only selects them.
    /// </para>
    /// <para>
    /// The built-in client also voices a dialogue (several speakers in one request). No
    /// <see cref="ISpeechDialogueSynthesizer"/> is registered: code takes the capability
    /// from <see cref="ISpeechProviderResolver.GetDialogueSynthesizerAsync"/>, which reads
    /// it from the bound synthesizer and applies the tenant's checks. A synthesizer of
    /// your own that implements the interface is used instead; one that does not is
    /// reported, not replaced by the built-in client.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseVoice(this ITraconBuilder builder, Action<VoiceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        // 🚨 Read BEFORE the TryAdd calls below: after them, the built-in
        // registrations look exactly like the consumer's own (F-336).
        services.TryAddSingleton(VoiceProviderBinding.Capture(services));

        services.Configure(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<VoiceOptions>, VoiceOptionsValidator>());
        UnreadableConfigurationValidator.Add<VoiceOptions>(services);

        // A single client serves synthesis, transcription, and health checks:
        // the same instance is bound to all three interfaces so the
        // concurrency limit is held in ONE counter. Separate instances would
        // allow twice the intended number of requests.
        services.TryAddSingleton(static provider =>
            new ElevenLabsSpeechClient(provider.GetRequiredService<IOptions<VoiceOptions>>().Value));

        services.TryAddSingleton<ISpeechSynthesizer>(static provider =>
            provider.GetRequiredService<ElevenLabsSpeechClient>());

        services.TryAddSingleton<ISpeechTranscriber>(static provider =>
            provider.GetRequiredService<ElevenLabsSpeechClient>());

        // The health check follows the BOUND synthesizer, not the built-in
        // client: a consumer's provider reports its own name and reachability.
        services.TryAddSingleton<IVoiceHealthCheck>(static provider =>
        {
            var synthesizer = provider.GetRequiredService<ISpeechSynthesizer>();

            return synthesizer as IVoiceHealthCheck
                   ?? new SynthesizerVoiceHealthCheck(
                       synthesizer,
                       provider.GetService<ILoggerFactory>()?.CreateLogger<SynthesizerVoiceHealthCheck>());
        });

        services.TryAddSingleton<VoicePricing>();

        // The HTTP layer reads the price through this abstraction; it cannot
        // reference Tracon.Voice (package direction rule).
        services.TryAddSingleton<IVoicePricingReader>(static provider =>
            provider.GetRequiredService<VoicePricing>());

        var requireApproval = ReadApprovalSetting(configure);

        // 🚨 Tools are registered with a FACTORY, not a ready instance: they
        // take their dependencies from the provider at setup time. Resolving
        // at call time is NOT POSSIBLE — Microsoft Agent Framework passes the
        // tool an EmptyServiceProvider (measured, see VoiceToolBase).
        services.AddSingleton(provider =>
            new TraconToolRegistration(new SpeakTool(provider)) { RequiresApproval = requireApproval });

        services.AddSingleton(provider =>
            new TraconToolRegistration(new TranscribeTool(provider)) { RequiresApproval = requireApproval });

        // Listing incurs no cost and has no side effect; the approval setting
        // does not apply to it.
        services.AddSingleton(provider =>
            new TraconToolRegistration(new ListVoicesTool(provider)));

        return builder;
    }

    /// <summary>
    /// Reads the approval setting BEFORE tool registration.
    /// </summary>
    /// <remarks>
    /// The approval flag is read while <c>ToolRegistry</c> is built, and the
    /// registry is built once from <c>TraconToolRegistration</c> entries;
    /// so the value cannot be resolved at run time through
    /// <see cref="IOptions{TOptions}"/>. The setting is taken here, with its
    /// value at registration time.
    /// </remarks>
    private static bool ReadApprovalSetting(Action<VoiceOptions> configure)
    {
        var probe = new VoiceOptions();
        configure(probe);

        return probe.RequireApproval;
    }

    /// <summary>
    /// Binds the <c>Tracon:Voice</c> section by hand.
    /// </summary>
    /// <remarks>
    /// Hand binding is an AOT requirement: <c>Bind()</c> uses reflection and
    /// leaves settings silently empty in trimmed applications.
    /// </remarks>
    private static void BindOptions(IConfiguration section, VoiceOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        if (section[nameof(VoiceOptions.Provider)] is { Length: > 0 } provider)
        {
            options.Provider = provider;
        }

        if (section[nameof(VoiceOptions.ApiKey)] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        read.Uri(
            nameof(VoiceOptions.Endpoint),
            static o => o.Endpoint,
            static (o, v) => o.Endpoint = v);

        if (section[nameof(VoiceOptions.DefaultVoiceId)] is { Length: > 0 } voiceId)
        {
            options.DefaultVoiceId = voiceId;
        }

        if (section[nameof(VoiceOptions.SynthesisModelId)] is { Length: > 0 } synthesisModel)
        {
            options.SynthesisModelId = synthesisModel;
        }

        if (section[nameof(VoiceOptions.TranscriptionModelId)] is { Length: > 0 } transcriptionModel)
        {
            options.TranscriptionModelId = transcriptionModel;
        }

        if (section[nameof(VoiceOptions.DialogueModelId)] is { Length: > 0 } dialogueModel)
        {
            options.DialogueModelId = dialogueModel;
        }

        if (section[nameof(VoiceOptions.OutputFormat)] is { Length: > 0 } outputFormat)
        {
            options.OutputFormat = outputFormat;
        }

        read.Int32(
            nameof(VoiceOptions.MaxCharactersPerRequest),
            static o => o.MaxCharactersPerRequest,
            static (o, v) => o.MaxCharactersPerRequest = v);

        read.Int32(
            nameof(VoiceOptions.MaxDialogueCharactersPerRequest),
            static o => o.MaxDialogueCharactersPerRequest,
            static (o, v) => o.MaxDialogueCharactersPerRequest = v);

        read.Int32(
            nameof(VoiceOptions.MaxConcurrentRequests),
            static o => o.MaxConcurrentRequests,
            static (o, v) => o.MaxConcurrentRequests = v);

        read.Boolean(
            nameof(VoiceOptions.RequireApproval),
            static o => o.RequireApproval,
            static (o, v) => o.RequireApproval = v);

        read.TimeSpan(
            nameof(VoiceOptions.Timeout),
            static o => o.Timeout,
            static (o, v) => o.Timeout = v);
    }
}
