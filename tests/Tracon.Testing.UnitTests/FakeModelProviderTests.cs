using Tracon.Testing;
using Microsoft.Extensions.AI;

namespace Tracon.Testing.UnitTests;

public sealed class FakeModelProviderTests
{
    private static readonly ModelBinding Binding = new() { Provider = "fake", Model = "fake-model" };

    [Fact]
    public async Task Default_setup_returns_a_fixed_response()
    {
        using var provider = new FakeModelProvider();

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        response.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EchoesUserMessage_echoes_the_last_user_message()
    {
        using var provider = new FakeModelProvider().EchoesUserMessage();

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "where is ORD-7")]);

        response.Text.ShouldContain("where is ORD-7", Case.Sensitive);
    }

    [Fact]
    public async Task RespondsWith_returns_responses_in_order()
    {
        using var provider = new FakeModelProvider().RespondsWith("first", "second");
        var client = provider.CreateChatClient(Binding);

        var first = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        var second = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        first.Text.ShouldContain("first", Case.Sensitive);
        second.Text.ShouldContain("second", Case.Sensitive);
    }

    [Fact]
    public async Task EchoesUserMessage_kicks_in_once_RespondsWith_queue_is_drained()
    {
        using var provider = new FakeModelProvider().RespondsWith("first").EchoesUserMessage();
        var client = provider.CreateChatClient(Binding);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);
        var third = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "next message")]);

        third.Text.ShouldContain("next message", Case.Sensitive);
    }

    [Fact]
    public async Task CallsTool_produces_a_FunctionCallContent()
    {
        using var provider = new FakeModelProvider().CallsTool("get_order_status", new { orderId = "ORD-7" });

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(
            [new ChatMessage(ChatRole.User, "where is ORD-7")]);

        var call = response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .ShouldHaveSingleItem();

        call.Name.ShouldBe("get_order_status");
        call.Arguments.ShouldNotBeNull();
        call.Arguments!["orderId"]!.ToString().ShouldBe("ORD-7");
    }

    [Fact]
    public async Task ForModel_gives_different_models_independent_queues()
    {
        using var provider = new FakeModelProvider()
            .ForModel("router-model", cfg => cfg.RespondsWith("router response"))
            .ForModel("researcher-model", cfg => cfg.RespondsWith("researcher response"));

        var routerResponse = await provider
            .CreateChatClient(Binding with { Model = "router-model" })
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        var researcherResponse = await provider
            .CreateChatClient(Binding with { Model = "researcher-model" })
            .GetResponseAsync([new ChatMessage(ChatRole.User, "x")]);

        routerResponse.Text.ShouldContain("router response", Case.Sensitive);
        researcherResponse.Text.ShouldContain("researcher response", Case.Sensitive);
    }

    [Fact]
    public async Task EchoesLastToolResult_echoes_the_last_tool_result_once_the_queue_is_drained()
    {
        using var provider = new FakeModelProvider()
            .CallsTool("get_order_status", new { orderId = "ORD-7" })
            .EchoesLastToolResult("Result: ");

        // 🚨 The tool-call loop moved to ModelProviderRegistry in Phase 48:
        // IModelProvider now returns the RAW client. This test therefore runs
        // through the real path — over the pipeline.
        using var client = new ModelProviderRegistry([provider]).CreateChatClient(Binding);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "where is ORD-7")],
            new ChatOptions
            {
                Tools =
                [
                    AIFunctionFactory.Create(
                        static (string orderId) => $"preparing ({orderId})",
                        "get_order_status"),
                ],
            });

        response.Text.ShouldContain("Result: preparing (ORD-7)", Case.Sensitive);
    }

    [Fact]
    public void WithModel_adds_the_given_descriptor_to_the_Models_list()
    {
        using var provider = new FakeModelProvider()
            .WithModel(new ModelDescriptor { Name = "custom-model", ContextWindowTokens = 4_096 });

        provider.Models.ShouldContain(model => string.Equals(model.Name, "custom-model", StringComparison.Ordinal));
    }
}
