using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>The content guard's behavior on the HTTP surface (Phase 48).</summary>
/// <remarks>
/// 🚨 <c>422</c> is possible only on the <strong>non-streaming</strong> branch. The
/// streaming branch is the default, and SSE headers are sent before the run
/// starts; because the guard sits in the model pipeline, the decision forms
/// AFTER the status code is written. The non-streaming branch is selected with
/// the <c>Idempotency-Key</c> header (Phase 43).
/// </remarks>
public sealed class ContentGuardEndpointTests
{
    private const string DeniedTerm = "secret-project";
    private const string CardNumber = "4539578763621486";

    private static readonly Uri Run = new("/agentprism/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Audit = new("/agentprism/api/audit?action=content.blocked", UriKind.Relative);

    [Fact]
    public async Task Block_hitting_the_input_returns_422()
    {
        await using var host = await StartAsync();

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = $"what is {DeniedTerm}" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        problem.GetProperty("title").GetString().ShouldBe("Content blocked");
        problem.GetProperty("errorType").GetString().ShouldBe("content_blocked");
        problem.GetProperty("guard").GetString().ShouldBe("pattern");
        problem.GetProperty("rule").GetString().ShouldBe("denied-term");
        problem.GetProperty("direction").GetString().ShouldBe("Input");
    }

    [Fact]
    public async Task ProblemDetails_does_not_carry_the_blocked_text()
    {
        // 🚨 The denied-term list is a corporate secret; writing it into the
        // response body would leak it to the client.
        await using var host = await StartAsync();

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { Message = $"the product code-named {DeniedTerm}" });

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain(DeniedTerm, Case.Insensitive);
        body.ShouldNotContain("the product code-named", Case.Insensitive);
    }

    [Fact]
    public async Task Block_writes_content_blocked_to_the_run_record()
    {
        await using var host = await StartAsync();

        using (var response = await PostBufferedAsync(host, new AgentRunRequest { Message = DeniedTerm }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/agentprism/api/runs?errorType=content_blocked", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var run = runs.ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe("content_blocked");
        run.Error.Class.ShouldBe(RunErrorClass.ContentBlocked);
    }

    [Fact]
    public async Task Block_does_not_trip_the_circuit_breaker()
    {
        // 🚨 The provider must NOT TRIP when pre-blocks stack up repeatedly: a
        // policy decision must not turn into an outage.
        await using var host = await StartAsync();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = DeniedTerm });

            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        // Behavioral verification: a harmless request must still pass. If the
        // circuit had tripped, this request would fail with
        // AgentPrismProviderUnavailableException.
        using var afterwards = await PostBufferedAsync(host, new AgentRunRequest { Message = "harmless prompt" });

        afterwards.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Services
            .GetRequiredService<ModelProviderCircuitBreaker>()
            .IsOpen("fake", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Audit_log_writes_the_rule_name_not_the_text()
    {
        await using var host = await StartAsync();

        using (var response = await PostBufferedAsync(host, new AgentRunRequest { Message = $"details of {DeniedTerm}" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var entries = await host.Client.GetFromJsonAsync<List<AuditEntry>>(
            Audit,
            TestContext.Current.CancellationToken);

        var entry = entries.ShouldHaveSingleItem();

        entry.Action.ShouldBe("content.blocked");
        entry.After.ShouldNotBeNull();
        entry.After!.ShouldContain("denied-term", Case.Sensitive);
        entry.After!.ShouldNotContain(DeniedTerm, Case.Insensitive);
    }

    [Fact]
    public async Task Masking_does_not_interrupt_the_run_and_writes_an_event()
    {
        await using var host = await StartAsync();

        using (var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { Message = $"my card number is {CardNumber}" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/agentprism/api/runs", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var run = runs.ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Completed);

        var events = await host.Client.GetStringAsync(
            new Uri($"/agentprism/api/runs/{run.Id}/events", UriKind.Relative),
            TestContext.Current.CancellationToken);

        events.ShouldContain("ContentMasked", Case.Sensitive);

        // 🚨 The guard's OWN event carries no content. Measurement: extract the
        // ContentMasked lines and search for the card number.
        //
        // 🚨 The SAME cannot be said for the WHOLE stream, and this is a deliberate
        // boundary: the RunStarted event carries the user's raw prompt (Phase 45,
        // the sole source of production-to-eval case promotion). Masking is a
        // control AT THE MODEL BOUNDARY; it does not retroactively clean
        // AgentPrism's own records. That is a separate task (retention/redaction)
        // and out of scope for this phase.
        var maskedLines = events
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.Contains("ContentMasked", StringComparison.Ordinal))
            .ToList();

        maskedLines.ShouldNotBeEmpty();

        foreach (var line in maskedLines)
        {
            line.ShouldNotContain(CardNumber, Case.Sensitive);
        }
    }

    [Fact]
    public async Task Behavior_is_unchanged_when_the_guard_is_off()
    {
        // The default setup registers no guard at all: the K1 gate is the registration itself.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = $"what is {DeniedTerm}" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Services.GetServices<IContentGuard>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Block_on_the_streaming_branch_becomes_an_SSE_error_event()
    {
        // 🚨 Deviation from the plan: 422 is physically impossible on the streaming
        // path. SSE headers are sent before the run starts, and the status code is
        // 200. The block appears in the stream as an 'error' event; the run record
        // still gets content_blocked written to it.
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = DeniedTerm },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("AgentPrismContentBlockedException", Case.Sensitive);
        body.ShouldNotContain(DeniedTerm, Case.Insensitive);
    }

    private static Task<AgentPrismTestHost> StartAsync()
        => AgentPrismTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .AddPatternContentGuard(static options =>
            {
                options.MaskedPii = PiiPatterns.CreditCard;
                options.DeniedTerms.Add(DeniedTerm);
            }));

    /// <summary>
    /// Enters the non-streaming branch. A request carrying the
    /// <c>Idempotency-Key</c> header runs with a single JSON response instead
    /// of SSE (Phase 43), and a status code can only be returned on that branch.
    /// </summary>
    private static async Task<HttpResponseMessage> PostBufferedAsync(AgentPrismTestHost host, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
