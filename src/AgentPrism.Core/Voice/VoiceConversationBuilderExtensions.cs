using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that enable the real-time voice conversation layer.</summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>This call changes the hosting model.</strong> A voice
/// connection stays open for minutes and binds to <em>one</em> server
/// instance; a multi-instance deployment requires sticky sessions, and the
/// reverse proxy must allow WebSocket passthrough. For this reason the
/// feature is opt-in: if the call is not made, no WebSocket endpoint is
/// opened and behavior does not change.
/// </para>
/// <para>
/// The layer is <strong>provider-agnostic</strong>: it uses only the
/// <see cref="ISpeechTranscriber"/> and <see cref="ISpeechSynthesizer"/>
/// abstractions. This call does <em>not</em> register them — a voice
/// provider must be enabled separately (e.g. <c>UseVoice(...)</c>).
/// </para>
/// </remarks>
public static class VoiceConversationBuilderExtensions
{
    /// <summary>Enables the conversation layer, reading its settings from configuration.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configurationSection">The <c>AgentPrism:Voice:Conversation</c> section.</param>
    /// <param name="configure">Changes applied after configuration binding.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public static IAgentPrismBuilder UseVoiceConversation(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection,
        Action<VoiceConversationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseVoiceConversation(options =>
        {
            BindOptions(configurationSection, options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Enables the conversation layer.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">The settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registrations are made with <c>TryAdd*</c>: if the consumer registered
    /// its own <see cref="IVoiceSessionStore"/> implementation before this
    /// call, theirs is preserved.
    /// </para>
    /// <para>
    /// 🚨 The transcription and synthesis providers are resolved <em>optionally</em>
    /// with <see cref="ServiceProviderServiceExtensions.GetService{T}(IServiceProvider)"/>.
    /// Requesting a nullable dependency through constructor injection is not
    /// enough: the built-in DI container can treat an unregistered type as
    /// required even when a C# default value exists
    /// (<c>docs/hafiza/aspnetcore-di.md</c>). This is why a factory is used.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UseVoiceConversation(
        this IAgentPrismBuilder builder,
        Action<VoiceConversationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IVoiceSessionStore, InMemoryVoiceSessionStore>();

        services.TryAddSingleton(static provider => new VoiceConversationDriver(
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentSessionManager>(),
            provider.GetRequiredService<ChatHistoryProvider>(),
            provider.GetRequiredService<IVoiceSessionStore>(),
            provider.GetRequiredService<IAttachmentStore>(),
            provider.GetRequiredService<AttachmentTypeGuard>(),
            provider.GetRequiredService<IOptions<VoiceConversationOptions>>(),
            provider.GetRequiredService<ILogger<VoiceConversationDriver>>(),
            provider.GetService<ISpeechTranscriber>(),
            provider.GetService<ISpeechSynthesizer>(),
            provider.GetService<TimeProvider>()));

        return builder;
    }

    /// <summary>
    /// Manually binds the <c>AgentPrism:Voice:Conversation</c> section.
    /// </summary>
    /// <remarks>
    /// Manual binding is an AOT requirement: <c>Bind()</c> uses reflection,
    /// and in trimmed applications the settings would silently stay empty
    /// (<c>docs/hafiza/build-ve-analyzer.md</c>).
    /// </remarks>
    private static void BindOptions(IConfiguration section, VoiceConversationOptions options)
    {
        if (TryInt(section[nameof(VoiceConversationOptions.MaxConcurrentConnectionsPerTenant)], out var connections))
        {
            options.MaxConcurrentConnectionsPerTenant = connections;
        }

        if (TryTimeSpan(section[nameof(VoiceConversationOptions.MaxConnectionDuration)], out var duration))
        {
            options.MaxConnectionDuration = duration;
        }

        if (TryTimeSpan(section[nameof(VoiceConversationOptions.IdleTimeout)], out var idle))
        {
            options.IdleTimeout = idle;
        }

        if (TryTimeSpan(section[nameof(VoiceConversationOptions.MaxUtteranceDuration)], out var utterance))
        {
            options.MaxUtteranceDuration = utterance;
        }

        if (TryInt(section[nameof(VoiceConversationOptions.MaxUtteranceBytes)], out var utteranceBytes))
        {
            options.MaxUtteranceBytes = utteranceBytes;
        }

        if (bool.TryParse(section[nameof(VoiceConversationOptions.PersistAudio)], out var persist))
        {
            options.PersistAudio = persist;
        }

        if (section[nameof(VoiceConversationOptions.VoiceId)] is { Length: > 0 } voiceId)
        {
            options.VoiceId = voiceId;
        }

        if (section[nameof(VoiceConversationOptions.OutputMediaType)] is { Length: > 0 } mediaType)
        {
            options.OutputMediaType = mediaType;
        }

        if (TryInt(section[nameof(VoiceConversationOptions.InputSampleRate)], out var sampleRate))
        {
            options.InputSampleRate = sampleRate;
        }

        if (TryInt(section[nameof(VoiceConversationOptions.MaxSpokenCharactersPerTurn)], out var spoken))
        {
            options.MaxSpokenCharactersPerTurn = spoken;
        }
    }

    private static bool TryInt(string? raw, out int value)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryTimeSpan(string? raw, out TimeSpan value)
        => TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out value);
}
