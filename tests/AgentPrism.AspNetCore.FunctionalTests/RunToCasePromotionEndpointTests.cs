using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// End-to-end tests for <c>POST /api/evals/{name}/cases/from-run/{runId}</c>
/// (Phase 45, F-53).
/// </summary>
public sealed class RunToCasePromotionEndpointTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    [Fact]
    public async Task Successful_run_is_promoted_as_a_reference_and_an_audit_record_is_written()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "hello", "session-1");
        await SaveSuiteAsync(host, "kod-agent");

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("query").GetString().ShouldBe("hello");
        body.GetProperty("expectedOutput").GetString()!.ShouldContain("Echo: hello");
        body.GetProperty("sourceKind").GetString().ShouldBe("ReferenceRun");
        body.GetProperty("sourceRunId").GetGuid().ShouldBe(runId);
        body.GetProperty("seq").GetInt32().ShouldBe(0);

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { TenantId = "default", Action = "eval.case.promoted" });
        entries.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Promoting_the_same_run_a_second_time_returns_200_and_creates_no_second_case()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "hello", "session-2");
        await SaveSuiteAsync(host, "kod-agent");

        using var first = await host.Client.PostAsync(PromoteUri(runId), content: null);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstCaseId = (await AgentPrismTestHost.ReadJsonAsync(first)).GetProperty("id").GetGuid();

        using var second = await host.Client.PostAsync(PromoteUri(runId), content: null);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondCaseId = (await AgentPrismTestHost.ReadJsonAsync(second)).GetProperty("id").GetGuid();

        secondCaseId.ShouldBe(firstCaseId);

        using var cases = await host.Client.GetAsync(CasesUri);
        (await AgentPrismTestHost.ReadJsonAsync(cases)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Failed_run_is_promoted_and_expectedOutput_is_empty()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder
                .AddModelProvider(new ThrowingModelProvider())
                .AddAgent(TestData.Definition(name: "broken-agent") with
                {
                    Model = new ModelBinding { Provider = "throws", Model = "throws-1" },
                }));

        var runId = await RunAsync(host, "hello", "session-3", agentName: "broken-agent", expectFailure: true);
        await SaveSuiteAsync(host, "broken-agent");

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("sourceKind").GetString().ShouldBe("FailedRun");
        body.GetProperty("expectedOutput").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Negatively_scored_run_is_promoted_and_expectedOutput_is_empty()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "hello", "session-4");
        await SaveSuiteAsync(host, "kod-agent");

        using (var feedback = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/runs/{runId}/feedback", UriKind.Relative),
            new { kind = "Binary", value = 0 }))
        {
            feedback.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("sourceKind").GetString().ShouldBe("NegativeScore");
        body.GetProperty("expectedOutput").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Multi_turn_run_is_rejected_with_409()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        await RunAsync(host, "first turn", "session-5");
        var secondRunId = await RunAsync(host, "second turn", "session-5");
        await SaveSuiteAsync(host, "kod-agent");

        using var response = await host.Client.PostAsync(PromoteUri(secondRunId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_sessionless_run_can_also_be_promoted()
    {
        // The query is read from run_events (RunStarted.Text), not from the
        // session (Phase 45): a sessionless run (no sessionId given) must
        // also be promotable.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "hello", sessionId: null);
        await SaveSuiteAsync(host, "kod-agent");

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("query").GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task A_run_with_an_empty_event_stream_is_accepted_without_a_query_returns_422()
    {
        // Without a RunStarted event (a hand-seeded legacy/broken record) the
        // query cannot be read; this verifies the NoQuery path is still reachable.
        await using var host = await AgentPrismTestHost.StartAsync();
        await SaveSuiteAsync(host, "test-agent");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Another_tenants_run_cannot_be_promoted_returns_the_SAME_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            });

        var runId = await RunAsAsync(host, "hello", "session-6", "tenant-a");
        await SaveSuiteAsAsync(host, "kod-agent", "tenant-a");
        await SaveSuiteAsAsync(host, "kod-agent", "tenant-b");

        using var missing = await SendAsTenant(host, PromoteUri(AgentPrismId.NewId()), "tenant-b");
        using var wrongTenant = await SendAsTenant(host, PromoteUri(runId), "tenant-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var ownTenant = await SendAsTenant(host, PromoteUri(runId), "tenant-a");
        ownTenant.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_run_neither_failed_nor_completed_requires_an_explicit_sourceKind()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        await SaveSuiteAsync(host, "test-agent");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Canceled,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        using var response = await host.Client.PostAsync(PromoteUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Nonexistent_suite_returns_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var runId = await RunAsync(host, "hello", "session-7");

        using var response = await host.Client.PostAsync(
            new Uri("/agentprism/api/evals/no-such-thing/cases/from-run/" + runId, UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static readonly Uri CasesUri = new("/agentprism/api/evals/customer-support-team/cases", UriKind.Relative);

    private static Uri PromoteUri(Guid runId)
        => new($"/agentprism/api/evals/customer-support-team/cases/from-run/{runId}", UriKind.Relative);

    private static async Task SaveSuiteAsync(AgentPrismTestHost host, string agentName)
    {
        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/evals/customer-support-team", UriKind.Relative),
            new EvalSuiteSaveRequest
            {
                AgentName = agentName,
                Checks = System.Text.Json.JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
            });

        response.EnsureSuccessStatusCode();
    }

    private static async Task SaveSuiteAsAsync(AgentPrismTestHost host, string agentName, string tenant)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put, new Uri("/agentprism/api/evals/customer-support-team", UriKind.Relative));
        request.Headers.Add(TenantHeader, tenant);
        request.Content = JsonContent.Create(new EvalSuiteSaveRequest
        {
            AgentName = agentName,
            Checks = System.Text.Json.JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
        });

        using var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Makes a run and returns the resulting run record's id.</summary>
    private static async Task<Guid> RunAsync(
        AgentPrismTestHost host,
        string message,
        string? sessionId,
        string agentName = "kod-agent",
        bool expectFailure = false)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/agentprism/api/agents/{agentName}/run", UriKind.Relative),
            new AgentRunRequest { Message = message, SessionId = sessionId });

        response.EnsureSuccessStatusCode();

        if (expectFailure)
        {
            // Errors are also carried as an event in the SSE stream; the
            // whole stream must be read for the run to close.
            try
            {
                await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
            }
            catch
            {
                // Expected: the stream closes with an error event.
            }
        }
        else
        {
            await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        }

        using var runs = await host.Client.GetAsync(new Uri("/agentprism/api/runs", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        json.GetArrayLength().ShouldBeGreaterThan(0);

        return json[0].GetProperty("id").GetGuid();
    }

    private static async Task<Guid> RunAsAsync(AgentPrismTestHost host, string message, string sessionId, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/agentprism/api/agents/kod-agent/run");
        request.Headers.Add(TenantHeader, tenant);
        request.Content = JsonContent.Create(new AgentRunRequest { Message = message, SessionId = sessionId });

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        }

        var runsRequest = new HttpRequestMessage(HttpMethod.Get, "/agentprism/api/runs");
        runsRequest.Headers.Add(TenantHeader, tenant);

        using var runs = await host.Client.SendAsync(runsRequest);
        var json = await AgentPrismTestHost.ReadJsonAsync(runs);

        return json[0].GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendAsTenant(AgentPrismTestHost host, Uri uri, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request);
    }

    /// <summary>A chat client that throws <see cref="InvalidOperationException"/> on every call.</summary>
    private sealed class ThrowingModelProvider : IModelProvider
    {
        public string Name => "throws";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "throws-1" }];

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null) => new ThrowingChatClient();

        private sealed class ThrowingChatClient : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("test: model deliberately fails.");

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("test: model deliberately fails.");

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }
}
