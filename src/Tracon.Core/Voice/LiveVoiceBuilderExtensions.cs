using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that enable the provider-hosted live voice layer.</summary>
/// <remarks>
/// <para>
/// <strong>This call changes the hosting model.</strong> A live session binds to
/// one server instance for as long as it lasts, so a multi-instance deployment needs
/// sticky routing. It is therefore opt-in: without the call no live route is mapped
/// and nothing about the application changes.
/// </para>
/// <para>
/// The call is <strong>independent</strong> of <c>UseVoiceConversation()</c>.
/// That layer has Tracon transcribe, run and synthesize; this one has the
/// provider host the whole conversation and hand work back. They are different
/// designs for different providers and can be enabled together or separately.
/// </para>
/// <para>
/// This call registers no provider. A live voice provider is enabled separately —
/// for example <c>UseOpenAILive(...)</c> — and until one is, the live endpoints
/// answer <c>501</c>.
/// </para>
/// </remarks>
public static class LiveVoiceBuilderExtensions
{
    /// <summary>Enables the live voice layer, reading its settings from configuration.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">The <c>Tracon:Voice:Live</c> section.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    ///        .UseOpenAILive(builder.Configuration.GetSection(OpenAILiveOptions.SectionName))
    ///        .UseLiveVoice(builder.Configuration.GetSection(VoiceLiveOptions.SectionName));
    /// </code>
    /// </example>
    public static ITraconBuilder UseLiveVoice(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return UseLiveVoiceCore(builder, options => Bind(configurationSection, options));
    }

    /// <summary>Enables the live voice layer with default settings.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ITraconBuilder UseLiveVoice(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return UseLiveVoiceCore(builder, configure: null);
    }

    /// <summary>Enables the live voice layer with settings given in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseLiveVoice(
        this ITraconBuilder builder,
        Action<VoiceLiveOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return UseLiveVoiceCore(builder, configure);
    }

    private static ITraconBuilder UseLiveVoiceCore(
        ITraconBuilder builder,
        Action<VoiceLiveOptions>? configure)
    {
        var services = builder.Services;

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddTraconDefault<IVoiceSessionStore, InMemoryVoiceSessionStore>();
        services.TryAddSingleton<VoiceDurationPricing>();

        services.TryAddSingleton(static provider =>
        {
            var registry = new LiveVoiceSessionRegistry(
                provider.GetRequiredService<IOptionsMonitor<VoiceLiveOptions>>(),
                provider.GetRequiredService<ILogger<LiveVoiceSessionRegistry>>(),
                provider.GetService<TimeProvider>());

            registry.Start();

            return registry;
        });

        // 🚨 A factory, not constructor injection. The built-in container treats an
        // unregistered constructor parameter as REQUIRED even when the parameter has
        // a C# default value, so an optional dependency — here the live provider and
        // the content guard — must be resolved with GetService inside a factory.
        services.TryAddSingleton(static provider => new LiveVoiceSessionLauncher(
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentSessionManager>(),
            provider.GetRequiredService<ChatHistoryProvider>(),
            provider.GetRequiredService<IVoiceSessionStore>(),
            provider.GetRequiredService<IOptionsMonitor<VoiceLiveOptions>>(),
            provider.GetRequiredService<VoiceDurationPricing>(),
            provider.GetRequiredService<LiveVoiceSessionRegistry>(),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetService<ILiveVoiceProvider>(),
            provider.GetService<ContentGuardPipeline>(),
            provider.GetService<TimeProvider>()));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, LiveVoiceShutdownService>());

        return builder;
    }

    /// <summary>Binds the live voice section by hand.</summary>
    /// <remarks>
    /// Hand-written because <c>Bind</c> uses reflection and the package must stay
    /// AOT-clean. The cost of hand-binding is that a forgotten key does not fail — it
    /// silently falls back to the default — so every property added above must be
    /// added here as well.
    /// </remarks>
    private static void Bind(IConfiguration section, VoiceLiveOptions options)
    {
        if (TryInt(section["MaxConcurrentDelegations"], out var maxConcurrentDelegations))
        {
            options.MaxConcurrentDelegations = maxConcurrentDelegations;
        }

        if (TryInt(section["MaxAppendsPerDelegation"], out var maxAppends))
        {
            options.MaxAppendsPerDelegation = maxAppends;
        }

        if (TryInt(section["MaxTranscriptLedgerCharacters"], out var maxLedgerCharacters))
        {
            options.MaxTranscriptLedgerCharacters = maxLedgerCharacters;
        }

        if (TryInt(section["MaxLedgerEntriesPerDelegation"], out var maxLedgerEntries))
        {
            options.MaxLedgerEntriesPerDelegation = maxLedgerEntries;
        }

        if (TryTimeSpan(section["DelegationTimeout"], out var delegationTimeout))
        {
            options.DelegationTimeout = delegationTimeout;
        }

        if (Enum.TryParse<LiveVoiceDelegationMode>(section["DelegationMode"], ignoreCase: true, out var mode))
        {
            options.DelegationMode = mode;
        }

        if (section["Instructions"] is { Length: > 0 } instructions)
        {
            options.Instructions = instructions;
        }

        if (bool.TryParse(section["PersistTranscript"], out var persistTranscript))
        {
            options.PersistTranscript = persistTranscript;
        }

        if (TryTimeSpan(section["MaxSessionDuration"], out var maxSessionDuration))
        {
            options.MaxSessionDuration = maxSessionDuration;
        }

        if (TryTimeSpan(section["PendingSessionTimeout"], out var pendingSessionTimeout))
        {
            options.PendingSessionTimeout = pendingSessionTimeout;
        }

        if (TryInt(section["MaxConcurrentSessionsPerTenant"], out var maxSessions))
        {
            options.MaxConcurrentSessionsPerTenant = maxSessions;
        }
    }

    private static bool TryInt(string? value, out int result)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryTimeSpan(string? value, out TimeSpan result)
        => TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result);
}

/// <summary>Closes every live voice session when the application stops.</summary>
/// <remarks>
/// Without this a shutdown drops the sockets and every open session's record stays
/// unwritten, so the conversations look as if they never ended.
/// </remarks>
internal sealed class LiveVoiceShutdownService : IHostedService
{
    private readonly LiveVoiceSessionRegistry _registry;

    /// <summary>Creates the service.</summary>
    /// <param name="registry">The session registry.</param>
    public LiveVoiceShutdownService(LiveVoiceSessionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => _registry.ShutdownAsync();
}
