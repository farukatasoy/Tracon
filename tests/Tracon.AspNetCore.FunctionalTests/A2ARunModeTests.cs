using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An A2A <c>SendMessage</c> is answered with a message, never with a
/// task.
/// </summary>
/// <remarks>
/// <para>
/// Tracon cannot suspend a run for approval mid-run, and an A2A task
/// is exactly that shape: a handle the caller polls while the run goes on in
/// the background. Microsoft Agent Framework 1.20 had a switch that turned
/// background responses off (<c>AllowBackgroundResponses = false</c>); 1.22
/// replaced it with <c>AgentRunMode</c>, and 1.25 no longer writes the old
/// switch at all. Nothing else in the suite looks at the response's kind, so
/// a registration that silently fell back to the framework default would still
/// pass every other A2A test.
/// </para>
/// </remarks>
public sealed class A2ARunModeTests
{
    [Fact]
    public async Task SendMessage_answers_with_a_message_and_not_a_task()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(static options => options.ExposedAgents.Add("kod-agent")),
            configureAfterMap: static app => app.MapTraconA2A());

        var (response, body) = await SendMessageAsync(host.Client, "kod-agent", "hello");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());

        var result = body.GetProperty("result");

        result.TryGetProperty("message", out var message).ShouldBeTrue($"the answer is a message: {body}");
        result.TryGetProperty("task", out _).ShouldBeFalse($"the answer is not a task: {body}");
        message.GetProperty("role").GetString().ShouldNotBeNull();
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> SendMessageAsync(
        HttpClient client,
        string agentName,
        string text)
    {
        // Built with the SDK's own types and serializer options, as in
        // A2AEndpointTests: the field names and casing come from the contract.
        var sendMessageRequest = new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText(text)],
                MessageId = Guid.NewGuid().ToString(),
            },
        };

        var paramsJson = JsonSerializer.SerializeToElement(sendMessageRequest, A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{agentName}")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = paramsJson,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response, JsonDocument.Parse(body).RootElement.Clone());
    }
}
