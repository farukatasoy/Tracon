using AgentPrism.Testing;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.UnitTests;

public sealed class FakeModelProviderRequestTests
{
    private static readonly ModelBinding Binding = new() { Provider = "fake", Model = "fake-model" };

    [Fact]
    public async Task Gelen_istekler_mesaj_ve_secenekleri_dogru_kaydeder()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);
        var options = new ChatOptions { Temperature = 0.5f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "merhaba")], options);

        var request = provider.Requests.ShouldHaveSingleItem();

        request.Messages.ShouldContain(message => message.Role == ChatRole.User && message.Text == "merhaba");
        request.Options.ShouldBe(options);
        request.IsStreaming.ShouldBeFalse();
    }

    [Fact]
    public async Task Akisli_cagri_IsStreaming_true_kaydeder()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "merhaba")]))
        {
            // Akisi tuketmek yeterli; icerik bu testin konusu degil.
        }

        var request = provider.Requests.ShouldHaveSingleItem();
        request.IsStreaming.ShouldBeTrue();
    }

    [Fact]
    public async Task En_yeni_istek_listenin_sonuncusudur()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "birinci")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ikinci")]);

        provider.Requests.Count.ShouldBe(2);
        provider.Requests[^1].Messages.ShouldContain(message => message.Text == "ikinci");
    }
}
