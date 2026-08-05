using AgentPrism.Voice.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>
/// <c>speak</c> tool'unun ek yazma yolunu dogrular.
/// </summary>
/// <remarks>
/// Bu sinifin uc testi Faz 28'in en pahali bulgularini korur:
/// oturum kimliginin eke yazilmasi (G1), tur denetiminin tool tarafindan
/// cagrilmasi (G2) ve gercek MP3 baytinin taninmasi (G3).
/// </remarks>
public sealed class SpeakToolTests
{
    /// <summary>ID3 etiketi TASIMAYAN gercek bir MPEG cerceve basligi.</summary>
    /// <remarks>
    /// 0xFF 0xF3 = 11 bit senkron + MPEG2 + Layer III. Eski beyaz liste yalnizca
    /// uc sabit deger tanidigi icin bu bayt dizisi bilerek secildi.
    /// </remarks>
    private static readonly byte[] FrameSyncMp3 = [0xFF, 0xF3, 0x48, 0xC4, 0x00, 0x00];

    [Fact]
    public async Task Uretilen_ses_ekine_OTURUM_kimligi_yazilir()
    {
        // 🚨 G1. Oturumsuz yazilan bir eki saklama politikasi SAHIPSIZ sayar ve
        // kesim tarihinden sonra siler; oturum hala yasarken ses kaybolur.
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3);

        var scope = new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = "kiraci-1",
            SessionId = "oturum-42",
            AgentName = "sesli-asistan",
        };

        AgentPrismRunContext.SetCurrent(scope);

        try
        {
            _ = await InvokeAsync(services, "merhaba");
        }
        finally
        {
            AgentPrismRunContext.SetCurrent(null);
        }

        var saved = store.Saved.ShouldHaveSingleItem();
        saved.SessionId.ShouldBe("oturum-42");
        saved.RunId.ShouldBe(scope.RunId);
        saved.TenantId.ShouldBe("kiraci-1");
    }

    [Fact]
    public async Task Basliksiz_icerik_ek_deposuna_YAZILMAZ()
    {
        // 🚨 G2. IAttachmentStore.SaveAsync hicbir dogrulama yapmaz; dogrulama
        // HTTP katmanindadir. Tool guard'i KENDISI cagirmak zorundadir.
        var store = new RecordingAttachmentStore();

        // Ham PCM: hicbir sihirli bayta uymaz.
        var services = BuildServices(store, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await InvokeAsync(services, "merhaba"));

        exception.Message.ShouldContain("sihirli bayt");
        store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cerceve_senkronlu_MP3_taninir_ve_kaydedilir()
    {
        // 🚨 G3. Beyaz liste eskiden yalnizca ID3/FFFB/FFF3 taniyordu; gercek
        // cikti baska bir gecerli cerceve basligi tasiyabilir.
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3);

        var result = await InvokeAsync(services, "merhaba");

        store.Saved.ShouldHaveSingleItem().MediaType.ShouldBe("audio/mpeg");
        result.ShouldNotBeNull();
        result.ToString().ShouldNotBeNull().ShouldContain("attachmentId=");
    }

    [Fact]
    public async Task Sonuc_ham_ses_DEGIL_ek_kimligi_dondurur()
    {
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3);

        var result = (await InvokeAsync(services, "merhaba"))?.ToString();

        result.ShouldNotBeNull();
        result.Contains(store.Saved[0].Id.ToString(), StringComparison.Ordinal).ShouldBeTrue();

        // Ham ses sonuca konsaydi baglam penceresi base64 ile dolardi.
        result.Contains("audio/mpeg;base64", StringComparison.Ordinal).ShouldBeFalse();
        result.Length.ShouldBeLessThan(200);
    }

    [Fact]
    public async Task Karakter_siniri_asilirsa_metin_KIRPILMAZ_hata_verilir()
    {
        var store = new RecordingAttachmentStore();
        var services = BuildServices(store, FrameSyncMp3, options => options.MaxCharactersPerRequest = 5);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await InvokeAsync(services, "bu metin bes karakterden uzun"));

        exception.Message.ShouldContain("sinir 5");
        store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public void Tool_semasi_metni_zorunlu_kilar()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var schema = new SpeakTool(services).JsonSchema.GetRawText();

        schema.ShouldContain("\"text\"");
        schema.ShouldContain("\"required\"");
    }

    private static async ValueTask<object?> InvokeAsync(ServiceProvider services, string text)
    {
        var tool = new SpeakTool(services);

        var arguments = new AIFunctionArguments(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["text"] = text },
            StringComparer.Ordinal)
        {
            Services = services,
        };

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildServices(
        IAttachmentStore store,
        byte[] audioBytes,
        Action<VoiceOptions>? configure = null)
    {
        var voiceOptions = new VoiceOptions { ApiKey = "k", DefaultVoiceId = "ses-1" };
        configure?.Invoke(voiceOptions);

        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(audioBytes));

        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<ITenantContext>(new FixedTenantContext("kiraci-1"));
        services.AddSingleton(Options.Create(voiceOptions));
        services.AddSingleton(Options.Create(new AgentPrismOptions()));
        services.AddSingleton<AttachmentTypeGuard>();
        services.AddSingleton<VoicePricing>();
        services.AddSingleton<ISpeechSynthesizer>(
            new ElevenLabsSpeechClient(voiceOptions, new HttpClient(handler)));

        return services.BuildServiceProvider();
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    /// <summary>Kaydedilen ekleri bellekte tutan depo.</summary>
    private sealed class RecordingAttachmentStore : IAttachmentStore
    {
        public List<AttachmentDescriptor> Saved { get; } = [];

        public ValueTask<AttachmentDescriptor> SaveAsync(
            AttachmentContent content,
            CancellationToken cancellationToken = default)
        {
            var descriptor = new AttachmentDescriptor
            {
                Id = Guid.NewGuid(),
                TenantId = content.TenantId,
                SessionId = content.SessionId,
                RunId = content.RunId,
                FileName = content.FileName,
                MediaType = content.MediaType,
                ByteSize = content.Data.Length,
                Sha256 = "SAHTE",
                CreatedBy = content.CreatedBy,
                CreatedAt = DateTimeOffset.UnixEpoch,
            };

            Saved.Add(descriptor);

            return ValueTask.FromResult(descriptor);
        }

        public ValueTask<AttachmentDescriptor?> GetAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Saved.Find(entry => entry.Id == id));

        public ValueTask<Stream?> OpenReadAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream?>(null);

        public ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(
            AttachmentQuery query,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<AttachmentDescriptor>>(Saved);

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);

        public ValueTask<int> DeleteBySessionAsync(
            string tenantId,
            string sessionId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);
    }
}
