using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Akisli yanitta cikis denetiminin tamponlanmasini dogrular.
/// </summary>
/// <remarks>
/// Akisin dogal sinirini olcer: bir cerceve istemciye gonderildikten sonra geri
/// alinamaz. Bu yuzden cikis guard'i acikken akis tamponlanir; kismi bir cerceve
/// uzerinde desen eslesmez.
/// </remarks>
public sealed class ContentGuardStreamingTests
{
    private static readonly IReadOnlyList<ChatResponseUpdate> SplitCardNumber =
    [
        new ChatResponseUpdate(ChatRole.Assistant, "kart numarasi 4539"),
        new ChatResponseUpdate(ChatRole.Assistant, "5787"),
        new ChatResponseUpdate(ChatRole.Assistant, "6362"),
        new ChatResponseUpdate(ChatRole.Assistant, "1486 idi"),
    ];

    [Fact]
    public async Task Tamponlanan_akista_cercevelere_bolunmus_desen_kacmaz()
    {
        // 🚨 Fazin akis kararinin gerekcesi: hicbir cerceve tek basina
        // "4539578763621486" icermez. Tamponlanmadan desen KACARDI.
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Blocking("4539578763621486"));

        var seen = 0;

        await Should.ThrowAsync<AgentPrismContentBlockedException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "kart numarasi neydi")],
                cancellationToken: TestContext.Current.CancellationToken))
            {
                seen++;
            }
        });

        // 🚨 Hicbir cerceve istemciye gitmedi: tampon karar verilmeden bosalmaz.
        seen.ShouldBe(0);
    }

    [Fact]
    public async Task Tamponlama_kapaliyken_bolunmus_desen_kacar()
    {
        // Ayarin ne satin aldigini olcer. Bu davranis bir kusur degil, acik bir
        // tercihtir: sessizce yarim denetim yapmak denetim yapmamaktan kotudur,
        // bu yuzden secim gizli degil ayar olarak durur.
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions { BufferStreamingOutput = false },
            StubContentGuard.Blocking("4539578763621486"));

        var seen = 0;

        await foreach (var _ in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "kart numarasi neydi")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            seen++;
        }

        seen.ShouldBe(SplitCardNumber.Count);
    }

    [Fact]
    public async Task Tamponlanan_akista_maskeleme_toplam_metni_korur()
    {
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Masking("4539578763621486", "[redacted]"));

        var text = string.Empty;

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "kart numarasi neydi")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            text += update.Text;
        }

        text.ShouldBe("kart numarasi [redacted] idi");
    }

    [Fact]
    public async Task Tamponlanan_akista_metin_disi_icerik_korunur()
    {
        // Kullanim sayaci ve tool cagrilari yerinde kalmalidir: kayit ve maliyet
        // hesabi onlara dayanir.
        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates:
            [
                new ChatResponseUpdate(ChatRole.Assistant, "yasak metin"),
                new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 7 })]),
            ]),
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Masking("yasak", "***"));

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "selam")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.SelectMany(static update => update.Contents)
            .OfType<UsageContent>()
            .ShouldHaveSingleItem()
            .Details.InputTokenCount.ShouldBe(7);

        string.Concat(updates.Select(static update => update.Text)).ShouldBe("*** metin");
    }

    [Fact]
    public async Task Cikis_denetimi_kapaliyken_akis_tamponlanmaz()
    {
        // Canlilik korunur: cerceveler geldigi gibi akar.
        var guard = StubContentGuard.Blocking("asla-eslesmez");

        using var chatClient = Guarded(
            new FakeChatClient(streamingUpdates: SplitCardNumber),
            new AgentPrismContentGuardOptions { InspectOutput = false },
            guard);

        var seen = 0;

        await foreach (var _ in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "selam")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            seen++;
        }

        seen.ShouldBe(SplitCardNumber.Count);
        guard.SeenDirections.ShouldNotContain(ContentGuardDirection.Output);
    }

    [Fact]
    public async Task Akisli_yolda_giris_de_denetlenir()
    {
        var inner = new FakeChatClient(streamingUpdates: SplitCardNumber);

        using var chatClient = Guarded(
            inner,
            new AgentPrismContentGuardOptions(),
            StubContentGuard.Blocking("gizli-proje"));

        await Should.ThrowAsync<AgentPrismContentBlockedException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "gizli-proje nedir")],
                cancellationToken: TestContext.Current.CancellationToken))
            {
                // Hicbir cerceve beklenmiyor.
            }
        });

        inner.CallCount.ShouldBe(0);
    }

    private static IChatClient Guarded(
        FakeChatClient inner,
        AgentPrismContentGuardOptions options,
        params IContentGuard[] guards)
        => TestData
            .Providers(TestData.ContentGuards(options: options, guards: guards), new FakeModelProvider(inner))
            .CreateChatClient(TestData.Binding());
}
