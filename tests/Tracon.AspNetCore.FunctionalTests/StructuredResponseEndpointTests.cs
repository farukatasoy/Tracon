using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Testing;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The structured response validation seam's behavior on the HTTP surface
/// (docs/131). A dedicated <see cref="FakeModelProvider"/> is scripted per
/// test with an EXACT response text — the host's default "echo" provider
/// prefixes its reply with <c>"Echo: "</c>, which would itself break JSON
/// well-formedness and confuse what each test is actually proving.
/// </summary>
public sealed class StructuredResponseEndpointTests
{
    private static readonly Uri Run = new("/tracon/api/agents/structured-agent/run", UriKind.Relative);

    [Fact]
    public async Task Disabled_by_default_a_malformed_response_does_not_fail_the_run()
    {
        // K1: no Tracon:StructuredResponse:Enabled configuration at all.
        await using var host = await StartAsync("not valid json", enabled: false);

        using var response = await PostBufferedAsync(host);

        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        (await EventTypesAsync(host, run.Id)).ShouldNotContain("StructuredResponseRejected");
    }

    [Fact]
    public async Task Enabled_a_malformed_response_fails_the_run_with_the_structured_response_error_class()
    {
        await using var host = await StartAsync("not valid json at all");

        using var response = await PostBufferedAsync(host);

        // No bespoke ProblemDetails mapping exists for this exception (unlike
        // TraconContentBlockedException's 422): it falls into the SAME
        // generic "Agent run failed" 502 every other unmapped run-ending
        // exception gets on the buffered path. What matters is the run record.
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe("structured_response_invalid");
        run.Error.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);

        (await EventTypesAsync(host, run.Id)).ShouldContain("StructuredResponseRejected");
    }

    [Fact]
    public async Task Enabled_a_valid_response_completes_the_run_and_writes_no_event()
    {
        await using var host = await StartAsync("{\"answer\":42}");

        using var response = await PostBufferedAsync(host);

        response.EnsureSuccessStatusCode();

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Completed);
        run.Error.ShouldBeNull();
        (await EventTypesAsync(host, run.Id)).ShouldNotContain("StructuredResponseRejected");
    }

    [Fact]
    public async Task A_throwing_consumer_validator_rejects_fail_closed()
    {
        // The response IS well formed JSON — only the consumer's own validator rejects it.
        await using var host = await StartAsync(
            "{\"answer\":42}", configureServices: static services => services.AddSingleton<IStructuredResponseValidator>(new ThrowingValidator()));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);
    }

    [Fact]
    public async Task Streaming_branch_still_closes_the_run_as_failed_after_the_content_already_streamed()
    {
        await using var host = await StartAsync("not valid json");

        // No Idempotency-Key header: this hits the streaming (SSE) branch.
        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello" }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The (invalid) content still reached the client - it cannot be un-sent (docs/131 §131.5).
        body.ShouldContain("not valid json", Case.Sensitive);
        body.ShouldContain("TraconStructuredResponseException", Case.Sensitive);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);
        (await EventTypesAsync(host, run.Id)).ShouldContain("StructuredResponseRejected");
    }

    [Fact]
    public async Task The_raw_response_text_never_reaches_the_run_error_message_or_the_rejection_events_own_fields()
    {
        const string SecretLookingText = "internal-only-token-should-not-leak";

        await using var host = await StartAsync(SecretLookingText);

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Error!.Message.ShouldNotContain(SecretLookingText, Case.Insensitive);

        var eventsBody = await EventTypesAsync(host, run.Id);

        // 🚨 The WHOLE stream is not redacted: RunStarted legitimately carries
        // the raw user prompt (Phase 45's eval-promotion source). Only the
        // StructuredResponseRejected event's OWN line must exclude the raw
        // response - same boundary ContentGuardEndpointTests measures for
        // ContentBlocked/ContentMasked.
        var rejectedLines = eventsBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.Contains("StructuredResponseRejected", StringComparison.Ordinal))
            .ToList();

        rejectedLines.ShouldNotBeEmpty();

        foreach (var line in rejectedLines)
        {
            line.ShouldNotContain(SecretLookingText, Case.Insensitive);
        }
    }

    [Fact]
    public async Task A_validator_that_throws_a_cancellation_nobody_requested_fails_the_run()
    {
        await using var host = await StartAsync(
            "{\"answer\":42}", configureServices: static services => services.AddSingleton<IStructuredResponseValidator>(new SelfCancelingValidator()));

        using var response = await PostBufferedAsync(host);

        // 🚨 Phase 157 changed this. The validator throws an
        // OperationCanceledException while NOTHING was cancelled - the same
        // shape HttpClient produces on its own request timeout. It used to be
        // taken at face value: the run closed as Canceled, the endpoint's
        // cancellation handler wrote nothing, and the caller got ASP.NET
        // Core's default 200 with an empty body - a failure that looked like a
        // success. The status now comes from the run's own token, not from the
        // exception type, so this is a failure and says so.
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);

        // Still not a validation error: the validator never returned a verdict.
        run.Error?.Class.ShouldNotBe(RunErrorClass.StructuredResponseInvalid);
    }

    [Fact]
    public async Task A_sink_that_cannot_observe_the_rejection_event_still_fails_the_run()
    {
        // Observability must not block the reject decision (K-493's rule,
        // applied to this seam): an IRunEventSink that throws must still let
        // the run close as Failed with the event written to the store.
        await using var host = await StartAsync(
            "not valid json", configureServices: static services => services.AddSingleton<IRunEventSink>(new ThrowingRunEventSink()));

        using var response = await PostBufferedAsync(host);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var run = await SingleRunAsync(host);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);
        (await EventTypesAsync(host, run.Id)).ShouldContain("StructuredResponseRejected");
    }

    private static Task<TraconTestHost> StartAsync(
        string modelResponse, bool enabled = true, Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("structured").RespondsWith(modelResponse))
                .AddAgent(JsonSchemaAgent()),
            configureServices: services =>
            {
                services.Configure<TraconStructuredResponseOptions>(options => options.Enabled = enabled);
                configureServices?.Invoke(services);
            });

    private static AgentDefinition JsonSchemaAgent() => new()
    {
        Name = "structured-agent",
        DisplayName = "Structured Agent",
        Description = "An agent asking for a JSON Schema response, used by structured response tests.",
        Instructions = "Reply briefly.",
        Model = new ModelBinding
        {
            Provider = "structured",
            Model = "structured-1",
            ResponseFormat = new AgentResponseFormat
            {
                Kind = AgentResponseFormatKind.JsonSchema,
                Schema = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}").RootElement,
                SchemaName = "answer",
            },
        },
        Origin = AgentDefinitionOrigin.Code,
    };

    private static async Task<RunRecord> SingleRunAsync(TraconTestHost host)
    {
        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative), TestContext.Current.CancellationToken);

        return runs.ShouldHaveSingleItem();
    }

    private static async Task<string> EventTypesAsync(TraconTestHost host, Guid runId)
        => await host.Client.GetStringAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>
    /// Enters the non-streaming branch. A request carrying the
    /// <c>Idempotency-Key</c> header runs with a single JSON response instead
    /// of SSE (Phase 43).
    /// </summary>
    private static async Task<HttpResponseMessage> PostBufferedAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private sealed class ThrowingValidator : IStructuredResponseValidator
    {
        public ValueTask<StructuredResponseValidationResult> ValidateAsync(
            StructuredResponseValidationContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the consumer's own validator is broken");
    }

    private sealed class SelfCancelingValidator : IStructuredResponseValidator
    {
        public ValueTask<StructuredResponseValidationResult> ValidateAsync(
            StructuredResponseValidationContext context, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException("simulated: the run was canceled while validation was in flight");
    }

    /// <summary>A sink whose writes always throw - proves a broken observability target never blocks the reject decision.</summary>
    private sealed class ThrowingRunEventSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("sink unavailable");
    }
}
