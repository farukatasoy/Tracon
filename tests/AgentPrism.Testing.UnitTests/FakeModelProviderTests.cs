using AgentPrism.Testing;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.UnitTests;

public sealed class FakeModelProviderTests
{
    private static readonly ModelBinding Binding = new() { Provider = "fake", Model = "fake-model" };

    [Fact]
    public async Task Varsayilan_kurulum_sabit_bir_yanit_dondurur()
    {
        using var provider = new FakeModelProvider();

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "merhaba")]);

        response.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EchoesUserMessage_son_kullanici_mesajini_yankilar()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "ORD-7 nerede")]);

        response.Text.ShouldContain("ORD-7 nerede", Case.Sensitive);
    }

    [Fact]
    public async Task RespondsWith_yanitlari_sirayla_dondurur()
    {
        using var provider = new FakeModelProvider().RespondsWith("ilk", "ikinci");
        var client = provider.CreateChatClient(Binding);

        var first = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        var second = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        first.Text.ShouldContain("ilk", Case.Sensitive);
        second.Text.ShouldContain("ikinci", Case.Sensitive);
    }

    [Fact]
    public async Task RespondsWith_kuyrugu_tukendikten_sonra_EchoesUserMessage_devreye_girer()
    {
        using var provider = new FakeModelProvider().RespondsWith("ilk").EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        var third = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "sonraki mesaj")]);

        third.Text.ShouldContain("sonraki mesaj", Case.Sensitive);
    }

    [Fact]
    public async Task CallsTool_bir_FunctionCallContent_uretir()
    {
        using var provider = new FakeModelProvider().CallsTool("get_order_status", new { orderId = "ORD-7" });

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "ORD-7 nerede")]);

        var call = response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .ShouldHaveSingleItem();

        call.Name.ShouldBe("get_order_status");
        call.Arguments.ShouldNotBeNull();
        call.Arguments!["orderId"]!.ToString().ShouldBe("ORD-7");
    }

    [Fact]
    public async Task ForModel_farkli_modeller_bagimsiz_kuyruk_kullanir()
    {
        using var provider = new FakeModelProvider()
            .ForModel("router-model", cfg => cfg.RespondsWith("router yaniti"))
            .ForModel("researcher-model", cfg => cfg.RespondsWith("researcher yaniti"));

        var routerResponse = await provider
            .CreateChatClient(Binding with { Model = "router-model" })
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        var researcherResponse = await provider
            .CreateChatClient(Binding with { Model = "researcher-model" })
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        routerResponse.Text.ShouldContain("router yaniti", Case.Sensitive);
        researcherResponse.Text.ShouldContain("researcher yaniti", Case.Sensitive);
    }

    [Fact]
    public async Task EchoesLastToolResult_kuyruk_tukendikten_sonra_son_tool_sonucunu_yankilar()
    {
        using var provider = new FakeModelProvider()
            .CallsTool("get_order_status", new { orderId = "ORD-7" })
            .EchoesLastToolResult("Sonuc: ");

        var client = provider.CreateChatClient(Binding);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "ORD-7 nerede")],
            new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(
                        static (string orderId) => $"hazirlaniyor ({orderId})",
                        "get_order_status"),
                ],
            });

        response.Text.ShouldContain("Sonuc: hazirlaniyor (ORD-7)", Case.Sensitive);
    }

    [Fact]
    public void WithModel_saglanan_tanimi_Models_listesine_ekler()
    {
        using var provider = new FakeModelProvider()
            .WithModel(new ModelDescriptor { Name = "ozel-model", ContextWindowTokens = 4_096 });

        provider.Models.ShouldContain(model => string.Equals(model.Name, "ozel-model", StringComparison.Ordinal));
    }
}
