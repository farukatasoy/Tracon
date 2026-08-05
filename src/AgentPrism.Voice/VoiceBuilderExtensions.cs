using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Ses tool'larini AgentPrism zincirine ekler.</summary>
public static class VoiceBuilderExtensions
{
    /// <summary>Ses tool'larini yapilandirmadan okuyarak kaydeder.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configurationSection"><c>AgentPrism:Voice</c> bolumu.</param>
    /// <param name="configure">Yapilandirmadan sonra uygulanacak degisiklikler.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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

    /// <summary>Ses tool'larini kaydeder.</summary>
    /// <param name="builder">AgentPrism zinciri.</param>
    /// <param name="configure">Ayarlar.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// Kayit <c>TryAdd*</c> ile yapilir: tuketici kendi
    /// <see cref="ISpeechSynthesizer"/> veya <see cref="ISpeechTranscriber"/>
    /// uygulamasini bu cagridan ONCE kaydettiyse onunki korunur.
    /// </para>
    /// <para>
    /// Uc tool kaydedilir: <c>speak</c>, <c>transcribe</c>, <c>list_voices</c>.
    /// Tool'lar K-012'nin geregi olarak KODDA tanimlidir; arayuzden yalnizca
    /// secilirler.
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

        // Tek bir istemci hem sentez hem cozum hem saglik denetimi sunar:
        // eszamanlilik sinirinin TEK bir sayacta tutulmasi icin ayni ornek
        // uc arayuze de baglanir. Ayri ornekler sinirin iki kati istege izin
        // verirdi.
        services.TryAddSingleton(static provider =>
            new ElevenLabsSpeechClient(provider.GetRequiredService<IOptions<VoiceOptions>>().Value));

        services.TryAddSingleton<ISpeechSynthesizer>(static provider =>
            provider.GetRequiredService<ElevenLabsSpeechClient>());

        services.TryAddSingleton<ISpeechTranscriber>(static provider =>
            provider.GetRequiredService<ElevenLabsSpeechClient>());

        services.TryAddSingleton<IVoiceHealthCheck>(static provider =>
            provider.GetRequiredService<ElevenLabsSpeechClient>());

        services.TryAddSingleton<VoicePricing>();

        // HTTP katmani fiyati bu soyutlama uzerinden okur; AgentPrism.Voice'a
        // referans veremez (paket yonu kurali).
        services.TryAddSingleton<IVoicePricingReader>(static provider =>
            provider.GetRequiredService<VoicePricing>());

        var requireApproval = ReadApprovalSetting(configure);

        // 🚨 Tool'lar FABRIKA ile kaydedilir, hazir ornekle degil: bagimliliklari
        // kurulum anindaki saglayicidan alirlar. Cagri aninda cozmek MUMKUN
        // DEGILDIR — Microsoft Agent Framework tool'a EmptyServiceProvider
        // gecirir (olculdu, bkz. VoiceToolBase).
        services.AddSingleton(provider =>
            new AgentPrismToolRegistration(new SpeakTool(provider), requireApproval));

        services.AddSingleton(provider =>
            new AgentPrismToolRegistration(new TranscribeTool(provider), requireApproval));

        // Listeleme ucret uretmez ve dis etki yaratmaz; onay ayari buna
        // uygulanmaz.
        services.AddSingleton(provider =>
            new AgentPrismToolRegistration(new ListVoicesTool(provider)));

        return builder;
    }

    /// <summary>
    /// Onay ayarini tool kaydindan ONCE okur.
    /// </summary>
    /// <remarks>
    /// 🚨 Onay bayragi <c>ToolRegistry</c> kurulurken okunur ve defter
    /// <c>AgentPrismToolRegistration</c> kayitlarindan bir kez insa edilir;
    /// bu yuzden deger <see cref="IOptions{TOptions}"/> uzerinden calisma
    /// aninda cozulemez. Ayar burada, kayit anindaki degeriyle alinir.
    /// </remarks>
    private static bool ReadApprovalSetting(Action<VoiceOptions> configure)
    {
        var probe = new VoiceOptions();
        configure(probe);

        return probe.RequireApproval;
    }

    /// <summary>
    /// <c>AgentPrism:Voice</c> bolumunu elle baglar.
    /// </summary>
    /// <remarks>
    /// Elle baglama AOT gereksinimidir: <c>Bind()</c> yansima kullanir ve
    /// kirpilmis uygulamalarda ayarlar sessizce bos kalir
    /// (bkz. <c>docs/hafiza/build-ve-analyzer.md</c>).
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
