using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Guvenlik/icerik filtresiyle kesilmis bos yanitin acik bir hataya cevrilmesi.
/// </summary>
/// <remarks>
/// Dekorator <c>ModelProviderRegistry.CreateChatClient</c> tarafindan her istemciye
/// uygulanir; bu yuzden testler defter uzerinden kosar — sarmalama sirasinin
/// bozulmasi da yakalanmis olur.
/// </remarks>
public sealed class ContentFilterDetectingChatClientTests
{
    [Fact]
    public async Task Filtrelenmis_bos_yanit_content_filtered_hatasi_atar()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var exception = await Should.ThrowAsync<AgentPrismContentFilteredException>(
            () => chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "selam")], cancellationToken: TestContext.Current.CancellationToken));

        exception.ErrorType.ShouldBe("content_filtered");
        exception.ProviderName.ShouldBe("fake");
        exception.FinishReason.ShouldBe(ChatFinishReason.ContentFilter.Value);
    }

    [Fact]
    public async Task Filtrelenmis_ama_metin_iceren_yanit_gecer()
    {
        // Model metin uretip sonra kesildiyse kullanicinin elinde kismi bir cevap
        // vardir; onu hataya cevirmek bilgi kaybi olurdu.
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, "kismi cevap")],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "selam")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("kismi cevap");
    }

    [Fact]
    public async Task Filtresiz_bos_yanit_hata_atmaz()
    {
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
            FinishReason = ChatFinishReason.Stop,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "selam")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Akisli_filtrelenmis_bos_yanit_akis_sonunda_hata_atar()
    {
        using var chatClient = Registry(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty),
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { FinishReason = ChatFinishReason.ContentFilter },
        ]));

        await Should.ThrowAsync<AgentPrismContentFilteredException>(async () =>
        {
            await foreach (var _ in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "selam")],
                cancellationToken: TestContext.Current.CancellationToken))
            {
                // Cerceveler tuketilir; karar akisin SONUNDA verilir.
            }
        });
    }

    [Fact]
    public async Task Akisli_filtrelenmis_ama_metin_iceren_yanit_gecer()
    {
        using var chatClient = Registry(new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "kismi"),
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { FinishReason = ChatFinishReason.ContentFilter },
        ]));

        var text = string.Empty;

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "selam")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            text += update.Text;
        }

        text.ShouldBe("kismi");
    }

    [Fact]
    public async Task Tool_cagrisi_iceren_filtrelenmis_yanit_bos_sayilmaz()
    {
        // Metin yok ama bir tool cagrisi var: yanit kullanilabilirdir.
        using var chatClient = Registry(new FakeChatClient(_ => new ChatResponse
        {
            Messages = [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("1", "get_order", null)])],
            FinishReason = ChatFinishReason.ContentFilter,
        }));

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "selam")],
            cancellationToken: TestContext.Current.CancellationToken);

        // Faz 48'den beri defter tool cagri dongusunu de kurar: cozulemeyen cagri
        // icin MAF bir sonuc icerigi ekler. Onemli olan istisna ATILMAMASIDIR —
        // tool cagrisi tasiyan filtreli yanit bos sayilmadi.
        response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Devre_kesici_filtreyi_hata_saymaz()
    {
        // Filtrelenmis yanit saglayicinin SAGLIKLI oldugunu gosterir. Dekorator devre
        // kesicinin disinda durmalidir; icinde olsaydi esik sayisinca filtrelenen
        // istek saglayiciyi kapatirdi.
        var breaker = new ModelProviderCircuitBreaker(
            new StaticOptionsMonitor<AgentPrismOptions>(new AgentPrismOptions
            {
                CircuitBreaker = new AgentPrismCircuitBreakerOptions { Enabled = true, FailureThreshold = 2 },
            }));

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(new FakeChatClient(_ => new ChatResponse
            {
                Messages = [new ChatMessage(ChatRole.Assistant, string.Empty)],
                FinishReason = ChatFinishReason.ContentFilter,
            }))],
            breaker);

        using var chatClient = registry.CreateChatClient(Binding);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Should.ThrowAsync<AgentPrismContentFilteredException>(
                () => chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "selam")], cancellationToken: TestContext.Current.CancellationToken));
        }

        breaker.IsOpen("fake", out _).ShouldBeFalse();
    }

    private static ModelBinding Binding => new() { Provider = "fake", Model = "fake-model" };

    private static IChatClient Registry(FakeChatClient inner)
        => new ModelProviderRegistry([new FakeModelProvider(inner)]).CreateChatClient(Binding);
}
