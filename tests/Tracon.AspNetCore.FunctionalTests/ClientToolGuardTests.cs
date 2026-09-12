using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Proves a client-side tool result passes through the same content guard
/// pipeline as every other message (Phase 61, section 61.4).
/// </summary>
/// <remarks>
/// No new guard code was written for this: <c>ContentGuardMessageMasker</c>
/// already covers <c>FunctionResultContent</c> regardless of its origin, and
/// the message <see cref="ClientToolResultResolver"/> builds is sent through
/// the same <c>agent.RunAsync(messages, ...)</c> call as everything else.
/// This test proves the WIRING — that the new endpoint code does not
/// somehow bypass the existing pipeline — not the masking logic itself
/// (already covered at the unit level).
/// </remarks>
public sealed class ClientToolGuardTests
{
    private const string AgentName = "client-tool-guard-agent";
    private const string ToolName = "read_page_title";
    private const string DeniedTerm = "secret-project";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    [Fact]
    public async Task A_blocked_pattern_in_a_client_tool_result_is_caught_before_reaching_the_model()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("guard-model").CallsTool(ToolName))
            .AddClientTool(ToolName, "Reads the current browser page title.", EmptyObjectSchema)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Use the tool when asked about the page title.",
                Model = new ModelBinding { Provider = "guard-model", Model = "guard-1" },
                ToolNames = [ToolName],
            })
            .AddPatternContentGuard(static options => options.DeniedTerms.Add(DeniedTerm)));

        using var first = await PostBufferedAsync(
            host,
            new AgentRunRequest { SessionId = "s1", Message = "what is the page title?" });

        var callId = ExtractCallId(await TraconTestHost.ReadJsonAsync(first));

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest
            {
                SessionId = "s1",
                ToolResults = [new ClientToolResult { CallId = callId, Result = $"the title mentions {DeniedTerm}" }],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errorType").GetString().ShouldBe("content_blocked");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(DeniedTerm, Case.Insensitive);
    }

    private static string ExtractCallId(JsonElement runResult)
    {
        foreach (var message in runResult.GetProperty("response").GetProperty("messages").EnumerateArray())
        {
            foreach (var content in message.GetProperty("contents").EnumerateArray())
            {
                if (string.Equals(content.GetProperty("$type").GetString(), "functionCall", StringComparison.Ordinal))
                {
                    return content.GetProperty("callId").GetString()!;
                }
            }
        }

        throw new InvalidOperationException("The response carried no functionCall content.");
    }

    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
