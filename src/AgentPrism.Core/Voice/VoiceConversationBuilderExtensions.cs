using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Gercek zamanli konusma katmanini acan uzantilar.</summary>
/// <remarks>
/// <para>
/// ⚠️ <strong>Bu cagri barindirma modelini degistirir.</strong> Konusma
/// baglantisi dakikalarca acik kalir ve <em>bir</em> sunucu ornegine baglanir;
/// cok ornekli bir dagitimda yapiskan oturum (sticky session) gerekir ve ters
/// vekil WebSocket gecisine izin vermelidir. Bu yuzden yetenek istege baglidir:
/// cagri yapilmazsa hicbir WebSocket ucu acilmaz ve davranis degismez.
/// </para>
/// <para>
/// Katman <strong>saglayicidan bagimsizdir</strong>: yalnizca
/// <see cref="ISpeechTranscriber"/> ve <see cref="ISpeechSynthesizer"/>
/// soyutlamalarini kullanir. Bu cagri onlari <em>kaydetmez</em> — bir ses
/// saglayicisi ayrica acilmalidir (ornegin <c>UseVoice(...)</c>).
/// </para>
/// </remarks>
public static class VoiceConversationBuilderExtensions
{
    /// <summary>Konusma katmanini yapilandirmadan okuyarak acar.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection"><c>AgentPrism:Voice:Conversation</c> bolumu.</param>
    /// <param name="configure">Yapilandirmadan sonra uygulanacak degisiklikler.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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

    /// <summary>Konusma katmanini acar.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayarlar.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Kayitlar <c>TryAdd*</c> ile yapilir: tuketici kendi
    /// <see cref="IVoiceSessionStore"/> uygulamasini bu cagridan once
    /// kaydettiyse onunki korunur.
    /// </para>
    /// <para>
    /// 🚨 Cozum ve sentez saglayicilari <see cref="ServiceProviderServiceExtensions.GetService{T}(IServiceProvider)"/>
    /// ile <em>istege bagli</em> cozulur. Kurucu enjeksiyonuyla nullable bir
    /// bagimlilik istemek yetmez: yerlesik DI kabi, C# varsayilan degeri olsa
    /// bile kayitli olmayan bir tipi zorunlu sayabilir
    /// (<c>docs/hafiza/aspnetcore-di.md</c>). Bu yuzden fabrika kullanilir.
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
    /// <c>AgentPrism:Voice:Conversation</c> bolumunu elle baglar.
    /// </summary>
    /// <remarks>
    /// Elle baglama AOT gereksinimidir: <c>Bind()</c> yansima kullanir ve
    /// kirpilmis uygulamalarda ayarlar sessizce bos kalir
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
