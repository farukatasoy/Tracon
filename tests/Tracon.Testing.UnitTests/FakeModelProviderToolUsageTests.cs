using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Testing;

namespace Tracon.Testing.UnitTests;

/// <summary>
/// A turn that asks for tools reports its token usage the way a turn that answers does (F-690), so a test of the
/// token budget goes through the path the real provider takes.
/// </summary>
public sealed class FakeModelProviderToolUsageTests
{
    private static readonly ModelBinding Binding = new() { Provider = "fake", Model = "fake-model" };

    private static readonly ChatMessage[] Ask = [new(ChatRole.User, "where is ORD-7")];

    [Fact]
    public async Task CallsTool_with_usage_reports_it_on_the_turn_that_asks_for_the_tool()
    {
        using var provider = new FakeModelProvider().CallsTool("get_order_status", new { orderId = "ORD-7" }, inputTokens: 40, outputTokens: 12);

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(Ask);

        response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .ShouldHaveSingleItem()
            .Name.ShouldBe("get_order_status");

        var usage = response.Usage.ShouldNotBeNull();

        usage.InputTokenCount.ShouldBe(40);
        usage.OutputTokenCount.ShouldBe(12);
        usage.TotalTokenCount.ShouldBe(52);
    }

    [Fact]
    public async Task CallsTool_with_usage_reports_it_on_a_streamed_turn_as_well()
    {
        using var provider = new FakeModelProvider().CallsTool("get_order_status", arguments: null, inputTokens: 40, outputTokens: 12);

        var contents = new List<AIContent>();

        await foreach (var update in provider.CreateChatClient(Binding).GetStreamingResponseAsync(Ask))
        {
            contents.AddRange(update.Contents);
        }

        contents.OfType<FunctionCallContent>().ShouldHaveSingleItem();

        var usage = contents.OfType<UsageContent>().ShouldHaveSingleItem().Details;

        usage.InputTokenCount.ShouldBe(40);
        usage.OutputTokenCount.ShouldBe(12);
    }

    [Fact]
    public async Task CallsTools_with_usage_reports_it_once_for_the_turn_and_not_once_per_call()
    {
        using var provider = new FakeModelProvider().CallsTools(
            inputTokens: 40,
            outputTokens: 20,
            ("lookup_order", new { orderId = "ORD-7" }),
            ("lookup_customer", null));

        var response = await provider.CreateChatClient(Binding).GetResponseAsync(Ask);

        response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(static call => call.Name)
            .ShouldBe(["lookup_order", "lookup_customer"]);

        var usage = response.Usage.ShouldNotBeNull();

        usage.InputTokenCount.ShouldBe(40);
        usage.OutputTokenCount.ShouldBe(20);
    }

    [Fact]
    public async Task A_tool_call_without_usage_still_reports_none()
    {
        // The overloads without tokens are what they were: a test that does not care about usage sees none.
        using var provider = new FakeModelProvider()
            .CallsTool("get_order_status")
            .CallsTool("get_order_status", new { orderId = "ORD-7" })
            .CallsTools(("lookup_order", null), ("lookup_customer", null));
        var client = provider.CreateChatClient(Binding);

        for (var turn = 0; turn < 3; turn++)
        {
            (await client.GetResponseAsync(Ask)).Usage.ShouldBeNull();
        }
    }

    [Fact]
    public void CallsTools_with_usage_still_needs_a_call()
    {
        using var provider = new FakeModelProvider();

        Should.Throw<ArgumentException>(() => provider.CallsTools(inputTokens: 1, outputTokens: 1))
            .ParamName.ShouldBe("calls");
    }

    [Fact]
    public async Task The_usage_of_a_tool_turn_reaches_the_run_budget_through_the_real_pipeline()
    {
        // The path the token budget reads: the registry's pipeline wraps the raw client, and the budget ring records the
        // usage of every model call. A tool turn that reports tokens spends them, with no side channel.
        using var provider = new FakeModelProvider()
            .CallsTool("get_order_status", new { orderId = "ORD-7" }, inputTokens: 400, outputTokens: 100)
            .RespondsWith("Shipped.", inputTokens: 10, outputTokens: 5);

        var services = new ServiceCollection();
        services.AddTracon().AddModelProvider(provider);
        using var serviceProvider = services.BuildServiceProvider();
        using var client = serviceProvider.GetRequiredService<IModelProviderRegistry>().CreateChatClient(Binding);

        var budget = new AgentRunBudget { MaxTotalTokens = 10_000 };

        // Set in the body of the test: an ambient value written in a helper does not flow back to its caller.
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), Budget = budget });

        try
        {
            var response = await client.GetResponseAsync(
                Ask,
                new ChatOptions { Tools = [AIFunctionFactory.Create(static (string orderId) => $"shipped ({orderId})", "get_order_status")] },
                TestContext.Current.CancellationToken);

            response.Text.ShouldContain("Shipped.", Case.Sensitive);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        // 500 tokens of the tool turn and 15 of the answer.
        budget.ConsumedTokens.ShouldBe(515);
    }
}
