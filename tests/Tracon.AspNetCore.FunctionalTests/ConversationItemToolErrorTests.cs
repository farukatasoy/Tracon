using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The items of a conversation never carry the message of the exception a tool threw.
/// </summary>
/// <remarks>
/// The exception object does not survive the session's stored state, so a listing read from the
/// store has none. A chat history provider that keeps live objects hands one back, and its message
/// can carry a connection string or a path: the model gets "Error: Function failed." for the
/// same reason, and so does the caller of this endpoint.
/// </remarks>
public sealed class ConversationItemToolErrorTests
{
    [Fact]
    public void A_tool_result_that_carries_an_exception_lists_the_result_not_the_exception_message()
    {
        var message = new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-1", "Error: Function failed.") { Exception = new InvalidOperationException("password=hunter2") }]);

        var item = OpenAIConversationsEndpoints.ToItems(message).ShouldHaveSingleItem();

        item.Type.ShouldBe("function_call_output");
        item.Status.ShouldBe("incomplete");
        item.Output.ShouldBe("Error: Function failed.");
    }

    [Fact]
    public void A_tool_result_with_an_exception_and_no_result_lists_no_output()
    {
        var message = new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-1", result: null) { Exception = new InvalidOperationException("password=hunter2") }]);

        var item = OpenAIConversationsEndpoints.ToItems(message).ShouldHaveSingleItem();

        item.Status.ShouldBe("incomplete");
        item.Output.ShouldBeNull();
    }

    [Fact]
    public void A_tool_result_without_an_exception_lists_its_result()
    {
        var message = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "forty-two")]);

        var item = OpenAIConversationsEndpoints.ToItems(message).ShouldHaveSingleItem();

        item.Status.ShouldBe("completed");
        item.Output.ShouldBe("forty-two");
    }
}
