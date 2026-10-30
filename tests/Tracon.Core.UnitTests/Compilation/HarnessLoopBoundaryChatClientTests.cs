using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The seam removes an approval request that is already answered by a tool
/// result, and only that. The end-to-end proof is
/// <c>HarnessApprovalContinuationTests</c>.
/// </summary>
public sealed class HarnessLoopBoundaryChatClientTests
{
    private static readonly FunctionCallContent Call = new("call-1", "cancel_order");

    [Fact]
    public void A_request_whose_call_has_a_result_is_removed_and_the_history_is_not_modified()
    {
        var request = new ToolApprovalRequestContent("request-1", Call);
        var history = new ChatMessage(ChatRole.Assistant, [new TextContent("Asking."), request]);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Cancel it."),
            history,
            new(ChatRole.Assistant, [Call]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "canceled")]),
        };

        var cleaned = HarnessLoopBoundaryChatClient.WithoutAnsweredApprovalRequests(messages).ToList();

        cleaned.SelectMany(static message => message.Contents).OfType<ToolApprovalRequestContent>().ShouldBeEmpty();
        cleaned[1].Text.ShouldBe("Asking.");
        history.Contents.ShouldContain(request);
    }

    [Fact]
    public void A_message_left_empty_is_dropped()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new ToolApprovalRequestContent("request-1", Call)]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "canceled")]),
        };

        var cleaned = HarnessLoopBoundaryChatClient.WithoutAnsweredApprovalRequests(messages).ToList();

        cleaned.ShouldHaveSingleItem().Role.ShouldBe(ChatRole.Tool);
    }

    [Fact]
    public void A_pending_request_or_one_with_its_response_is_left_alone()
    {
        var pending = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new ToolApprovalRequestContent("request-1", Call)]),
        };

        var request = new ToolApprovalRequestContent("request-1", Call);
        var answered = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [request]),
            new(ChatRole.User, [request.CreateResponse(approved: true)]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "canceled")]),
        };

        HarnessLoopBoundaryChatClient.WithoutAnsweredApprovalRequests(pending).ShouldBeSameAs(pending);
        HarnessLoopBoundaryChatClient.WithoutAnsweredApprovalRequests(answered).ShouldBeSameAs(answered);
    }
}
