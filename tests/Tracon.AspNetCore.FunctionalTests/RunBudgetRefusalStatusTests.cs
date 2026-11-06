using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that the run budget stops is Tracon's own policy answer, not an upstream failure: every endpoint that
/// answers before a stream begins says <c>422</c> with the setting to raise, never <c>502 Bad Gateway</c>. A
/// <c>502</c> says "the provider is broken" and sends the caller looking at the wrong system.
/// </summary>
/// <remarks>
/// The turn that asks for the tool reports 600 tokens against a budget of 500, so the model call after the tool result is
/// the one the budget refuses. The replay endpoint has its own case in <see cref="RunReplayEndpointTests"/>; the streamed
/// answers tell the same refusal inside the stream.
/// </remarks>
public sealed class RunBudgetRefusalStatusTests
{
    private const string AgentName = "budgeted";
    private const string Provider = "budget-echo";
    private const string ErrorType = "run_budget_exceeded";

    [Fact]
    public async Task The_agent_run_endpoint_answers_422_when_the_budget_stops_a_run_that_is_not_streamed()
    {
        await using var host = await StartAsync();

        // 'Idempotency-Key' makes the run non-streaming: the answer is a status line, not an event stream.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "where is ORD-7" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("errorType").GetString().ShouldBe(ErrorType);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("Tracon:AgentGraph:MaxTotalTokens", Case.Sensitive);
    }

    [Fact]
    public async Task Chat_completions_answers_422_with_the_budget_error_type_when_the_budget_stops_a_run()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = AgentName, messages = new[] { new { role = "user", content = "where is ORD-7" } } },
            TestContext.Current.CancellationToken);

        await ShouldBeTheBudgetEnvelopeAsync(response);
    }

    [Fact]
    public async Task Responses_answers_422_with_the_budget_error_type_when_the_budget_stops_a_run()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = AgentName, input = "where is ORD-7" },
            TestContext.Current.CancellationToken);

        await ShouldBeTheBudgetEnvelopeAsync(response);
    }

    private static async Task ShouldBeTheBudgetEnvelopeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);

        var body = await TraconTestHost.ReadJsonAsync(response);
        var error = body.GetProperty("error");

        error.GetProperty("type").GetString().ShouldBe(ErrorType);
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("Tracon:AgentGraph:MaxTotalTokens", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(
            static builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider(Provider)
                        .CallsTools(inputTokens: 400, outputTokens: 200, ("get_order_status", new { orderId = "ORD-7" }))
                        .EchoesLastToolResult("Done: ", inputTokens: 4, outputTokens: 6))
                    .AddToolsFrom(typeof(ReplayProbeTools))
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = Provider, Model = "echo-1" },
                        ToolNames = ["get_order_status"],
                    });
            },
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.AgentGraph.MaxTotalTokens = 500));
}
