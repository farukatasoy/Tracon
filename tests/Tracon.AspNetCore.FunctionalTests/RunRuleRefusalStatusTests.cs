using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that ends because its own rule was not met (a loop that reached its ceiling with a criterion unsatisfied) is
/// answered <c>422</c> with the error type, never <c>502 Bad Gateway</c>, on every endpoint that answers before a stream
/// begins.
/// </summary>
/// <remarks>
/// The provider answered and the request was understood; a <c>502</c> says "the provider is broken" and sends the caller
/// looking at the wrong system, and the answer had no <c>errorType</c> to tell the caller which rule it was. The run record
/// holds the rest, so the problem document names the run. The streamed answers tell the same refusal inside the stream (an
/// SSE <c>error</c> frame), which is unchanged. The structured-response rejection has its own cases in
/// <c>StructuredResponseEndpointTests</c>; the replay endpoint has one here.
/// </remarks>
public sealed class RunRuleRefusalStatusTests
{
    private const string AgentName = "never-done";
    private const string ProviderName = "loop-echo";
    private const string ErrorType = "loop_unsatisfied";

    [Fact]
    public async Task The_agent_run_endpoint_answers_422_when_a_loop_ends_unsatisfied_and_the_run_is_not_streamed()
    {
        await using var host = await StartAsync();

        // 'Idempotency-Key' makes the run non-streaming: the answer is a status line, not an event stream.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{AgentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "go" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        var problem = System.Text.Json.JsonDocument.Parse(text).RootElement;

        problem.GetProperty("errorType").GetString().ShouldBe(ErrorType);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(AgentName, Case.Sensitive);

        // The answer names the run, and the run is the failed one.
        var run = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(problem.GetProperty("runId").GetGuid());

        run.ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(ErrorType);
    }

    [Fact]
    public async Task Chat_completions_answers_422_with_the_loop_error_type_when_a_loop_ends_unsatisfied()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = AgentName, messages = new[] { new { role = "user", content = "go" } } },
            TestContext.Current.CancellationToken);

        await ShouldBeTheLoopEnvelopeAsync(response);
    }

    [Fact]
    public async Task Responses_answers_422_with_the_loop_error_type_when_a_loop_ends_unsatisfied()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = AgentName, input = "go" },
            TestContext.Current.CancellationToken);

        await ShouldBeTheLoopEnvelopeAsync(response);
    }

    private static async Task ShouldBeTheLoopEnvelopeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);

        var error = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("error");

        error.GetProperty("type").GetString().ShouldBe(ErrorType);
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain(AgentName, Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(builder =>
        {
            var provider = new FakeModelProvider(ProviderName);
            provider.ForModel("loop-1", static cfg =>
            {
                for (var turn = 0; turn < 3; turn++)
                {
                    cfg.RespondsWith("working");
                }
            });

            builder.AddModelProvider(provider);
            builder.AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Work.",
                Model = new ModelBinding { Provider = ProviderName, Model = "loop-1" },
                Harness = new HarnessSettings
                {
                    Loop = new LoopSettings
                    {
                        Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "NEVER APPEARS" }],
                        MaxIterations = 2,
                        OnExhausted = LoopOutcome.FailRun,
                    },
                },
                Origin = AgentDefinitionOrigin.Code,
            });
        });
}
