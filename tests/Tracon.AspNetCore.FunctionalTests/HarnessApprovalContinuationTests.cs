using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ToolGovernanceEndpointTests.cs: the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The turn that answers a pending approval runs the approved tool and
/// completes, in a harness agent as in a plain one.
/// </summary>
/// <remarks>
/// <c>HarnessDefaultsTests.A_tool_that_requires_approval_waits_in_a_harness_agent</c>
/// covers only the first turn. The second turn failed in every harness agent
/// with <c>InvalidOperationException</c> from MEAI's
/// <c>FunctionInvokingChatClient</c>: "ToolApprovalRequestContent found with
/// FunctionCall.CallId(s) … that have no matching ToolApprovalResponseContent".
/// </remarks>
public sealed class HarnessApprovalContinuationTests
{
    private const string AgentName = "harness-approval";

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_approved_tool_runs_and_the_harness_turn_completes(bool harness)
    {
        var calls = 0;
        await using var host = await StartAsync(harness, () => Interlocked.Increment(ref calls));

        var sessionId = $"harness-approval-{harness}";

        using var pending = await host.Client.PostAsJsonAsync(
            RunUri,
            new AgentRunRequest { Message = "cancel the order", SessionId = sessionId });

        pending.StatusCode.ShouldBe(HttpStatusCode.OK);
        var requestId = await ReadApprovalRequestIdAsync(pending);
        calls.ShouldBe(0);

        using var answered = await host.Client.PostAsJsonAsync(
            RunUri,
            new AgentRunRequest
            {
                SessionId = sessionId,
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true }],
            });

        answered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await answered.Content.ReadAsStringAsync();

        body.ShouldContain("ORD-7 canceled.");
        body.ShouldNotContain("event: error");
        calls.ShouldBe(1);
    }

    /// <summary>The non-streaming path the queued approval resume takes.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_approved_or_rejected_tool_completes_a_buffered_harness_turn(bool approved)
    {
        var calls = 0;
        await using var host = await StartAsync(harness: true, () => Interlocked.Increment(ref calls));

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var agent = (await catalog.ResolveAsync(AgentName, culture: null, CancellationToken.None)).ShouldNotBeNull();
        var session = await agent.CreateSessionAsync();

        var first = await agent.RunAsync([new ChatMessage(ChatRole.User, "Cancel ORD-7.")], session);
        var request = first.Messages.SelectMany(static message => message.Contents)
            .OfType<ToolApprovalRequestContent>()
            .ShouldHaveSingleItem();

        var second = await agent.RunAsync([new ChatMessage(ChatRole.User, [request.CreateResponse(approved)])], session);

        calls.ShouldBe(approved ? 1 : 0);
        second.Messages.SelectMany(static message => message.Contents)
            .OfType<ToolApprovalRequestContent>()
            .ShouldBeEmpty();
    }

    /// <param name="harness">Whether the agent is a harness agent.</param>
    /// <param name="onCall">Runs when the approved tool does.</param>
    private static Task<TraconTestHost> StartAsync(bool harness, Action onCall)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("harness-approval-model")
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId =>
                {
                    onCall();
                    return $"{orderId} canceled.";
                }),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: static options => options.RequiresApproval = true)
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "harness-approval-model", Model = "harness-approval-1" },
                ToolNames = ["cancel_order"],
                Harness = harness ? new HarnessSettings() : null,
            }));

    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            if (string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                return System.Text.Json.JsonDocument.Parse(frame.Data)
                    .RootElement.EnumerateArray()
                    .Select(static request => request.GetProperty("requestId").GetString())
                    .First()
                    .ShouldNotBeNull();
            }
        }

        throw new ShouldAssertException("The run did not return a pending approval request.");
    }
}
