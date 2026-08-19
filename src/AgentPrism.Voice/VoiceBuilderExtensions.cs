using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Adds the voice tools to the AgentPrism chain.</summary>
public static class VoiceBuilderExtensions
{
    /// <summary>Registers the voice tools, reading settings from configuration.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configurationSection">The <c>AgentPrism:Voice</c> section.</param>
    /// <param name="configure">Changes to apply after configuration binding.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">When a dependency is <see langword="null"/>.</exception>
    /// <remarks>
    /// Registers the speech tools an agent can call. The real-time
    /// conversation layer is a separate, opt-in call
    /// (<c>UseVoiceConversation()</c>).
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseVoice(builder.Configuration.GetSection("AgentPrism:Voice"));
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder UseVoice(
        this IAgentPrismBuilder builder,
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
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">The settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">When a dependency is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registration uses <c>TryAdd*</c>: when the consumer registered its own
    /// <see cref="ISpeechSynthesizer"/> or <see cref="ISpeechTranscriber"/>
    /// implementation BEFORE this call, theirs is preserved.
    /// </para>
    /// <para>
    /// Three tools are registered: <c>speak</c>, <c>transcribe</c>, <c>list_voices</c>.
    /// Per K-012, tools are defined IN CODE; the UI only selects them.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseVoice(this IAgentPrismBuilder builder, Action<VoiceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.Configure(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<VoiceOptions>, VoiceOptionsValidator>());

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

        services.TryAddSingleton<IVoiceHealthCheck>(static provider =>
            provider.GetRequiredService<ElevenLabsSpeechClient>());

        services.TryAddSingleton<VoicePricing>();

        // The HTTP layer reads the price through this abstraction; it cannot
        // reference AgentPrism.Voice (package direction rule).
        services.TryAddSingleton<IVoicePricingReader>(static provider =>
            provider.GetRequiredService<VoicePricing>());

        var requireApproval = ReadApprovalSetting(configure);

        // 🚨 Tools are registered with a FACTORY, not a ready instance: they
        // take their dependencies from the provider at setup time. Resolving
        // at call time is NOT POSSIBLE — Microsoft Agent Framework passes the
        // tool an EmptyServiceProvider (measured, see VoiceToolBase).
        services.AddSingleton(provider =>
            new AgentPrismToolRegistration(new SpeakTool(provider), requireApproval));

        services.AddSingleton(provider =>
            new AgentPrismToolRegistration(new TranscribeTool(provider), requireApproval));

        // Listing incurs no cost and has no side effect; the approval setting
        // does not apply to it.
        services.AddSingleton(provider =>
            new AgentPrismToolRegistration(new ListVoicesTool(provider)));

        return builder;
    }

    /// <summary>
    /// Reads the approval setting BEFORE tool registration.
    /// </summary>
    /// <remarks>
    /// 🚨 The approval flag is read while <c>ToolRegistry</c> is built, and the
    /// registry is built once from <c>AgentPrismToolRegistration</c> entries;
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
    /// Binds the <c>AgentPrism:Voice</c> section by hand.
    /// </summary>
    /// <remarks>
    /// Hand binding is an AOT requirement: <c>Bind()</c> uses reflection and
    /// leaves settings silently empty in trimmed applications
    /// (see <c>docs/hafiza/build-ve-analyzer.md</c>).
    /// </remarks>
    private static void BindOptions(IConfiguration section, VoiceOptions options)
    {
        if (section[nameof(VoiceOptions.Provider)] is { Length: > 0 } provider)
        {
            options.Provider = provider;
        }

        if (section[nameof(VoiceOptions.ApiKey)] is { Length: > 0 } apiKey)
        {
            options.ApiKey = apiKey;
        }

        if (section[nameof(VoiceOptions.Endpoint)] is { Length: > 0 } endpoint &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out var parsedEndpoint))
        {
            options.Endpoint = parsedEndpoint;
        }

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

        if (section[nameof(VoiceOptions.OutputFormat)] is { Length: > 0 } outputFormat)
        {
            options.OutputFormat = outputFormat;
        }

        if (int.TryParse(
                section[nameof(VoiceOptions.MaxCharactersPerRequest)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxCharacters))
        {
            options.MaxCharactersPerRequest = maxCharacters;
        }

        if (int.TryParse(
                section[nameof(VoiceOptions.MaxConcurrentRequests)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrent))
        {
            options.MaxConcurrentRequests = maxConcurrent;
        }

        if (bool.TryParse(section[nameof(VoiceOptions.RequireApproval)], out var requireApproval))
        {
            options.RequireApproval = requireApproval;
        }

        if (TimeSpan.TryParse(section[nameof(VoiceOptions.Timeout)], CultureInfo.InvariantCulture, out var timeout))
        {
            options.Timeout = timeout;
        }
    }
}
