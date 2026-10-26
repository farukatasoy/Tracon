using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that enable the real-time voice conversation layer.</summary>
/// <remarks>
/// <para>
/// <strong>This call changes the hosting model.</strong> A voice
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
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">The <c>Tracon:Voice:Conversation</c> section.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    /// <remarks>
    /// A speech provider is registered separately; this call only opens the
    /// conversation layer over it.
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseVoice(builder.Configuration.GetSection("Tracon:Voice"))
    ///        .UseVoiceConversation(builder.Configuration.GetSection("Tracon:Voice:Conversation"));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseVoiceConversation(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseVoiceConversation(options => BindOptions(configurationSection, options));
    }

    /// <summary>Enables the conversation layer with default settings.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Registrations are made with <c>TryAdd*</c>: if the consumer registered
    /// its own <see cref="IVoiceSessionStore"/> implementation before this
    /// call, theirs is preserved.
    /// </para>
    /// <para>
    /// The transcription and synthesis providers are resolved <em>optionally</em>
    /// with <see cref="ServiceProviderServiceExtensions.GetService{T}(IServiceProvider)"/>.
    /// Requesting a nullable dependency through constructor injection is not
    /// enough: the built-in DI container can treat an unregistered type as
    /// required even when a C# default value exists.
    /// This is why a factory is used.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseVoiceConversation(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return UseVoiceConversationCore(builder, configure: null);
    }

    /// <summary>Enables the conversation layer with settings given in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseVoiceConversation(
        this ITraconBuilder builder,
        Action<VoiceConversationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return UseVoiceConversationCore(builder, configure);
    }

    private static ITraconBuilder UseVoiceConversationCore(
        ITraconBuilder builder,
        Action<VoiceConversationOptions>? configure)
    {
        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        UnreadableConfigurationValidator.Add<VoiceConversationOptions>(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<VoiceConversationOptions>, VoiceConversationOptionsValidator>());

        services.TryAddTraconDefault<IVoiceSessionStore, InMemoryVoiceSessionStore>();

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
            provider.GetRequiredService<ISpeechProviderResolver>(),
            provider.GetService<TimeProvider>()));

        return builder;
    }

    /// <summary>
    /// Manually binds the <c>Tracon:Voice:Conversation</c> section.
    /// </summary>
    /// <remarks>
    /// Manual binding is an AOT requirement: <c>Bind()</c> uses reflection,
    /// and in trimmed applications the settings would silently stay empty.
    /// </remarks>
    private static void BindOptions(IConfiguration section, VoiceConversationOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        read.Int32(
            nameof(VoiceConversationOptions.MaxConcurrentConnectionsPerTenant),
            static o => o.MaxConcurrentConnectionsPerTenant,
            static (o, v) => o.MaxConcurrentConnectionsPerTenant = v);

        read.TimeSpan(
            nameof(VoiceConversationOptions.MaxConnectionDuration),
            static o => o.MaxConnectionDuration,
            static (o, v) => o.MaxConnectionDuration = v);

        read.TimeSpan(
            nameof(VoiceConversationOptions.IdleTimeout),
            static o => o.IdleTimeout,
            static (o, v) => o.IdleTimeout = v);

        read.TimeSpan(
            nameof(VoiceConversationOptions.MaxUtteranceDuration),
            static o => o.MaxUtteranceDuration,
            static (o, v) => o.MaxUtteranceDuration = v);

        read.Int32(
            nameof(VoiceConversationOptions.MaxUtteranceBytes),
            static o => o.MaxUtteranceBytes,
            static (o, v) => o.MaxUtteranceBytes = v);

        read.Boolean(
            nameof(VoiceConversationOptions.PersistAudio),
            static o => o.PersistAudio,
            static (o, v) => o.PersistAudio = v);

        if (section[nameof(VoiceConversationOptions.VoiceId)] is { Length: > 0 } voiceId)
        {
            options.VoiceId = voiceId;
        }

        if (section[nameof(VoiceConversationOptions.OutputMediaType)] is { Length: > 0 } mediaType)
        {
            options.OutputMediaType = mediaType;
        }

        read.Int32(
            nameof(VoiceConversationOptions.InputSampleRate),
            static o => o.InputSampleRate,
            static (o, v) => o.InputSampleRate = v);

        read.Int32(
            nameof(VoiceConversationOptions.MaxSpokenCharactersPerTurn),
            static o => o.MaxSpokenCharactersPerTurn,
            static (o, v) => o.MaxSpokenCharactersPerTurn = v);
    }
}
