using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The rule behind a failed tool call in a <c>POST /v1/responses</c> answer, on its own: the exception leaves
/// the copy that is written, never the original, and everything that carries none passes through untouched.
/// The end-to-end behaviour is in <see cref="ToolFailureResponsesEndpointTests"/>.
/// </summary>
public sealed class ResponsesToolFailureTextTests
{
    private const string Text = "Server=db.internal;Password=canary-redaction-text"; // SYNTHETIC-CREDENTIAL: the text a tool's exception carries

    [Fact]
    public void A_failed_result_is_copied_without_its_exception_and_keeps_what_the_model_was_given()
    {
        var original = new FunctionResultContent("call_1", "Error: Function failed.")
        {
            Exception = new InvalidOperationException(Text),
        };
        var response = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_1", "lookup")]),
            new ChatMessage(ChatRole.Tool, [original]) { MessageId = "m_tool", AuthorName = "agent" },
        ])
        {
            ResponseId = "r_1",
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { TotalTokenCount = 7 },
        };

        var written = ResponsesToolFailureText.Redact(response);

        written.ShouldNotBeSameAs(response);
        var result = written.Messages[1].Contents.ShouldHaveSingleItem().ShouldBeOfType<FunctionResultContent>();
        result.CallId.ShouldBe("call_1");
        result.Result.ShouldBe("Error: Function failed.");
        result.Exception.ShouldBeNull();
        written.Messages[1].MessageId.ShouldBe("m_tool");
        written.Messages[1].AuthorName.ShouldBe("agent");
        written.Messages[0].ShouldBeSameAs(response.Messages[0], "a message without a failed result is the same object");
        written.ResponseId.ShouldBe("r_1");
        written.FinishReason.ShouldBe(ChatFinishReason.Stop);
        written.Usage!.TotalTokenCount.ShouldBe(7);

        original.Exception.ShouldNotBeNull("the run record has read the failure from the original, which stays as it was");
        response.Messages[1].Contents[0].ShouldBeSameAs(original);
    }

    [Fact]
    public void A_response_without_a_failed_result_is_returned_as_the_same_instance()
    {
        var response = new AgentResponse(
        [
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "ok")]),
            new ChatMessage(ChatRole.Assistant, "done"),
        ]);

        ResponsesToolFailureText.Redact(response).ShouldBeSameAs(response);
    }

    [Fact]
    public async Task A_streamed_update_with_a_failed_result_is_replaced_and_the_others_pass_through()
    {
        var plain = new AgentResponseUpdate(ChatRole.Assistant, "hello") { MessageId = "m_1" };
        var failed = new AgentResponseUpdate(
            ChatRole.Tool,
            [new FunctionResultContent("call_1", "Error: Function failed.") { Exception = new InvalidOperationException(Text) }])
        {
            MessageId = "m_2",
            ResponseId = "r_1",
            FinishReason = ChatFinishReason.ToolCalls,
        };

        var written = new List<AgentResponseUpdate>();

        await foreach (var update in ResponsesToolFailureText.Redact(Updates(plain, failed), TestContext.Current.CancellationToken))
        {
            written.Add(update);
        }

        written.Count.ShouldBe(2);
        written[0].ShouldBeSameAs(plain);
        written[1].ShouldNotBeSameAs(failed);
        written[1].Contents.ShouldHaveSingleItem().ShouldBeOfType<FunctionResultContent>().Exception.ShouldBeNull();
        written[1].Contents.OfType<FunctionResultContent>().Single().Result.ShouldBe("Error: Function failed.");
        written[1].MessageId.ShouldBe("m_2");
        written[1].ResponseId.ShouldBe("r_1");
        written[1].FinishReason.ShouldBe(ChatFinishReason.ToolCalls);
        failed.Contents.OfType<FunctionResultContent>().Single().Exception.ShouldNotBeNull();
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> Updates(params AgentResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
