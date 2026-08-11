using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>
/// <c>UseVoice(...)</c> kaydinin gercekten cozulebilir servisler biraktigini
/// dogrular.
/// </summary>
/// <remarks>
/// 🚨 Bu sinifin ilk testi ornek uygulamada bulunan gercek bir hatanin
/// gerileme testidir: tool calisma aninda
/// <c>No service for type 'IOptions&lt;VoiceOptions&gt;'</c> ile dustu.
/// Tool'lar DI'i CAGRI aninda cozer; kayit eksikligi ne derlemede ne de
/// kurulum sirasinda gorunur.
/// </remarks>
public sealed class VoiceBuilderExtensionsTests
{
    [Fact]
    public void Ayarlar_cagri_aninda_cozulebilir()
    {
        using var provider = BuildProvider(options =>
        {
            options.ApiKey = "k";
            options.DefaultVoiceId = "ses-1";
        });

        provider.GetService<IOptions<VoiceOptions>>().ShouldNotBeNull()
            .Value.DefaultVoiceId.ShouldBe("ses-1");
    }

    [Fact]
    public void Tool_bagimliliklarinin_TAMAMI_cozulebilir()
    {
        // Tool govdesinin istedigi her servis burada listelenir. Biri eksikse
        // hata yalnizca gercek bir tool cagrisinda gorunurdu.
        using var provider = BuildProvider(options => options.ApiKey = "k");

        provider.GetService<ISpeechSynthesizer>().ShouldNotBeNull();
        provider.GetService<ISpeechTranscriber>().ShouldNotBeNull();
        provider.GetService<IVoiceHealthCheck>().ShouldNotBeNull();
        provider.GetService<IVoicePricingReader>().ShouldNotBeNull();
        provider.GetService<IOptions<VoiceOptions>>().ShouldNotBeNull();
        provider.GetService<AttachmentTypeGuard>().ShouldNotBeNull();
        provider.GetService<IAttachmentStore>().ShouldNotBeNull();
        provider.GetService<ITenantContext>().ShouldNotBeNull();
    }

    [Fact]
    public void Uc_tool_kaydedilir()
    {
        using var provider = BuildProvider(options => options.ApiKey = "k");

        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.TryGet("speak", out _).ShouldBeTrue();
        registry.TryGet("transcribe", out _).ShouldBeTrue();
        registry.TryGet("list_voices", out _).ShouldBeTrue();
    }

    [Fact]
    public void Onay_ayari_uretim_tool_larina_uygulanir_listelemeye_uygulanmaz()
    {
        using var provider = BuildProvider(options =>
        {
            options.ApiKey = "k";
            options.RequireApproval = true;
        });

        var descriptors = provider.GetRequiredService<IToolRegistry>().List();

        Find(descriptors, "speak").RequiresApproval.ShouldBeTrue();
        Find(descriptors, "transcribe").RequiresApproval.ShouldBeTrue();

        // Listeleme ucret uretmez ve dis etki yaratmaz.
        Find(descriptors, "list_voices").RequiresApproval.ShouldBeFalse();
    }

    [Fact]
    public void Tuketicinin_kendi_uygulamasi_KORUNUR()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer, CustomSynthesizer>();
        services.AddAgentPrism().UseVoice(options => options.ApiKey = "k");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISpeechSynthesizer>().ShouldBeOfType<CustomSynthesizer>();
    }

    [Fact]
    public void Ayni_ornek_uc_arayuze_de_baglanir()
    {
        // Eszamanlilik siniri TEK bir sayacta tutulmalidir; ayri ornekler
        // sinirin iki katina izin verirdi.
        using var provider = BuildProvider(options => options.ApiKey = "k");

        var synthesizer = provider.GetRequiredService<ISpeechSynthesizer>();
        var transcriber = provider.GetRequiredService<ISpeechTranscriber>();
        var health = provider.GetRequiredService<IVoiceHealthCheck>();

        ReferenceEquals(synthesizer, transcriber).ShouldBeTrue();
        ReferenceEquals(synthesizer, health).ShouldBeTrue();
    }

    private static ToolDescriptor Find(IReadOnlyList<ToolDescriptor> descriptors, string name)
        => descriptors.Single(descriptor => string.Equals(descriptor.Name, name, StringComparison.Ordinal));

    private static ServiceProvider BuildProvider(Action<VoiceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseVoice(configure);

        return services.BuildServiceProvider();
    }

    private sealed class CustomSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "benimki";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }
}
