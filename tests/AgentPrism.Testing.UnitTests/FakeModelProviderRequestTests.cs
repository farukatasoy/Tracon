using AgentPrism.Testing;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.UnitTests;

public sealed class FakeModelProviderRequestTests
{
    private static readonly ModelBinding Binding = new() { Provider = "fake", Model = "fake-model" };

    [Fact]
    public async Task Incoming_requests_record_the_message_and_options_correctly()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);
        var options = new ChatOptions { Temperature = 0.5f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], options);

        var request = provider.Requests.ShouldHaveSingleItem();

        request.Messages.ShouldContain(message => message.Role == ChatRole.User && message.Text == "hello");
        request.Options.ShouldBe(options);
        request.IsStreaming.ShouldBeFalse();
    }

    [Fact]
    public async Task Streaming_call_records_IsStreaming_true()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
        {
            // Draining the stream is enough; content is not this test's concern.
        }

        var request = provider.Requests.ShouldHaveSingleItem();
        request.IsStreaming.ShouldBeTrue();
    }

    [Fact]
    public async Task Newest_request_is_last_in_the_list()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "first")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "second")]);

        provider.Requests.Count.ShouldBe(2);
        provider.Requests[^1].Messages.ShouldContain(message => message.Text == "second");
    }
}
