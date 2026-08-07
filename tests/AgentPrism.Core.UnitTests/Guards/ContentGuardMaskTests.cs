using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>Maskeleme kararinin modele ve istemciye yansimasini dogrular.</summary>
public sealed class ContentGuardMaskTests
{
    [Fact]
    public async Task Maskelenen_metin_modele_maskelenmis_gider()
    {
        var inner = new FakeChatClient();
        using var chatClient = Guarded(inner, StubContentGuard.Masking("4539578763621486", "[redacted]"));

        await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "kart numaram 4539578763621486")],
            cancellationToken: TestContext.Current.CancellationToken);

        var sent = inner.LastRequest.Single().Text;
        sent.ShouldBe("kart numaram [redacted]");
        sent.ShouldNotContain("4539578763621486", Case.Sensitive);
    }

    [Fact]
    public async Task Cagiranin_mesaj_listesi_degistirilmez()
    {
        // 🚨 Maskeleme modele giden istemi degistirir, konusma gecmisini
        // degistirmez. Gecmise yazilsaydi maske kalicilasir ve kullanicinin
        // kendi yazdigi metin geri alinamaz sekilde kaybolurdu.
        var original = new ChatMessage(ChatRole.User, "kart numaram 4539578763621486");
        var messages = new List<ChatMessage> { original };

        using var chatClient = Guarded(new FakeChatClient(), StubContentGuard.Masking("4539578763621486", "[redacted]"));

        await chatClient.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        messages.Single().ShouldBeSameAs(original);
        original.Text.ShouldContain("4539578763621486", Case.Sensitive);
    }

    [Fact]
    public async Task Maskelenen_mesajin_ham_gosterimi_dusurulur()
    {
        // 🚨 Bazi saglayici adaptorleri istegi ham gosterimden kurar (Faz 26'da
        // olculdu: Anthropic adaptoru verilen ham nesnenin uzerine YAZMIYOR).
        // Tasinsaydi maskelenmemis metin aga cikardi.
        var inner = new FakeChatClient();

        var message = new ChatMessage(ChatRole.User, "kart numaram 4539578763621486")
        {
            RawRepresentation = "ham govde: 4539578763621486",
        };

        using var chatClient = Guarded(inner, StubContentGuard.Masking("4539578763621486", "[redacted]"));

        await chatClient.GetResponseAsync([message], cancellationToken: TestContext.Current.CancellationToken);

        inner.LastRequest.Single().RawRepresentation.ShouldBeNull();

        // Cagiranin nesnesi bozulmadi.
        message.RawRepresentation.ShouldNotBeNull();
    }

    [Fact]
    public async Task Eslesme_yoksa_mesaj_nesnesi_yeniden_kurulmaz()
    {
        // Tahsis disiplini: eslesme olmayan yolda hicbir yeni ChatMessage uretilmez.
        var inner = new FakeChatClient();
        var original = new ChatMessage(ChatRole.User, "zararsiz istem");

        using var chatClient = Guarded(inner, StubContentGuard.Masking("asla-eslesmez", "***"));

        await chatClient.GetResponseAsync([original], cancellationToken: TestContext.Current.CancellationToken);

        inner.LastRequest.Single().ShouldBeSameAs(original);
    }

    [Fact]
    public async Task Cikis_maskelemesi_yanita_yansir()
    {
        using var chatClient = Guarded(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "iban TR330006100519786457841326"))
            {
                RawRepresentation = "ham yanit: TR330006100519786457841326",
            }),
            StubContentGuard.Masking("TR330006100519786457841326", "[redacted]"));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "iban nedir")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("iban [redacted]");
        response.RawRepresentation.ShouldBeNull();
    }

    [Fact]
    public async Task Tool_sonucu_maskelenir_ve_tipi_korunur()
    {
        // Tool sonucu bir FunctionResultContent'tir; maskelenirken TextContent'e
        // donusturulemez, yoksa MAF cagri kimligini kaybeder.
        var inner = new FakeChatClient();

        using var chatClient = Guarded(inner, StubContentGuard.Masking("sk-canli-anahtar", "[redacted]"));

        await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.User, "sonucu ozetle"),
                new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "anahtar sk-canli-anahtar")]),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        var result = inner.LastRequest[1].Contents.Single().ShouldBeOfType<FunctionResultContent>();
        result.CallId.ShouldBe("call-1");
        result.Result.ShouldBe("anahtar [redacted]");
    }

    private static IChatClient Guarded(FakeChatClient inner, params IContentGuard[] guards)
        => TestData
            .Providers(TestData.ContentGuards(guards: guards), new FakeModelProvider(inner))
            .CreateChatClient(TestData.Binding());
}
