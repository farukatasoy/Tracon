// An agent whose tool throws an exception that is not Tracon's own (here an InvalidOperationException whose message
// carries a connection string) is told "Error: Function failed." by the model call. POST /tracon/v1/responses, buffered
// and with stream:true, must not write that exception's type and message into the function_call_output item of its
// answer: the framework's writer does so for any tool result that carries an exception, and the answer's caller must
// not be handed more than the model was (ResponsesToolFailureText). The run endpoint and Chat Completions never did.
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

public sealed class ToolFailureResponsesEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_responses_endpoint_does_not_return_the_text_of_a_tool_exception_that_is_not_Tracons_own(bool stream)
    {
        const string Text = "canary-responses-tool-text";

        using var database = new TempSqliteDatabase("tool-failure-responses");

        await using var canary = await KeyedCanaryHost.StartAsync(
            CanaryKeyKind.Setup,
            "toolfail-responses",
            database,
            configure: builder => builder.AddTool(
                (Func<string>)(() => throw new InvalidOperationException($"Server=db.internal;Password={Text}")), // SYNTHETIC-CREDENTIAL: the text a tool's exception carries, searched for in every output
                name: KeyedCanaryHost.ToolName,
                description: "Looks an order up."));

        canary.Wire.ToolCall = KeyedCanaryHost.ToolName;
        canary.Wire.Mode = CanaryWireServer.Behavior.Accept;

        using var reply = await canary.Host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = KeyedCanaryHost.AgentName, stream, input = "hello" },
            TestContext.Current.CancellationToken);

        var body = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("function_call_output", Case.Sensitive, "the answer carries no tool result, so there is nothing to search");
        body.ShouldNotContain(Text, Case.Sensitive);
    }
}
