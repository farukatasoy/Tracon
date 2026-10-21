using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The host and the helpers of the tests that answer a tool approval inside a workflow (phase 230): a stored
/// chain of agents and a code graph, both hosted by Microsoft Agent Framework, over an agent whose tool needs
/// approval.
/// </summary>
/// <remarks>
/// The stored chain is saved with <c>agentNames</c>, the ready-made pattern. The node-list form is a
/// hand-built graph whose agent nodes are plain function executors (a mixed chain), and those cannot wait for an
/// approval; <see cref="WorkflowAgentStepRecordingTests"/> holds that.
/// </remarks>
internal static class ToolApprovalWorkflowHost
{
    /// <summary>The tool that needs approval.</summary>
    public const string Tool = "cancel_order";

    /// <summary>The stored chain: one agent.</summary>
    public const string StoredApproval = "approval-chain";

    /// <summary>The code graph: one hosted agent.</summary>
    public const string CodeApproval = "approval-code";

    /// <summary>Starts a host with both workflows over the writer agent and its tool.</summary>
    /// <param name="provider">The model provider.</param>
    /// <param name="calls">Counts how many times <see cref="Tool"/> ran.</param>
    /// <param name="configureServices">More service registrations.</param>
    /// <param name="configureTracon">More Tracon configuration.</param>
    /// <param name="saveStoredChain">
    /// Whether to save the stored chain; <see langword="false"/> for a second host over a database that already
    /// holds it.
    /// </param>
    /// <returns>The host.</returns>
    public static async Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        int[] calls,
        Action<IServiceCollection>? configureServices = null,
        Action<ITraconBuilder>? configureTracon = null,
        bool saveStoredChain = true)
    {
        var host = await AgentStepHost.StartAsync(
            provider,
            AgentStepHost.WriterAgent(format: null, Tool),
            builder =>
            {
                AddApprovalTool(builder, Tool, calls);

                // The code graph hosts the agent the way a ready-made pattern does.
                builder.AddWorkflow(
                    CodeApproval,
                    static services => AgentWorkflowBuilder.BuildSequential(
                        CodeApproval, [services.GetWorkflowAgent(CodeApproval, AgentStepHost.Agent)]));

                configureTracon?.Invoke(builder);
            },
            configureServices);

        if (saveStoredChain)
        {
            await SaveAsync(host, StoredApproval, WorkflowKind.Sequential, [AgentStepHost.Agent]);
        }

        return host;
    }

    /// <summary>Registers a tool that counts its runs and needs approval.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="name">The tool's name.</param>
    /// <param name="calls">Counts the runs.</param>
    public static void AddApprovalTool(ITraconBuilder builder, string name, int[] calls)
        => builder.AddTool(
            (Func<string, string>)(orderId =>
            {
                Interlocked.Increment(ref calls[0]);

                return $"{orderId} canceled.";
            }),
            name: name,
            description: "Cancels an order.",
            configure: options => options.RequiresApproval = true);

    /// <summary>Saves a stored workflow of the agent-names form.</summary>
    /// <param name="host">The host.</param>
    /// <param name="name">The workflow's name.</param>
    /// <param name="kind">The pattern.</param>
    /// <param name="agentNames">The agents.</param>
    /// <returns>A task that completes when the workflow is saved.</returns>
    public static async Task SaveAsync(TraconTestHost host, string name, WorkflowKind kind, IReadOnlyList<string> agentNames)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{name}",
            new WorkflowSaveRequest { Kind = kind, AgentNames = agentNames });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>Runs the workflow until it waits and reads the requests it published.</summary>
    /// <param name="host">The host.</param>
    /// <param name="workflow">The workflow.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The run and its requests.</returns>
    public static async Task<WaitingRun> RunToApprovalAsync(TraconTestHost host, string workflow, string? tenant = null)
    {
        using var started = await Send(
            host,
            HttpMethod.Post,
            $"/tracon/api/workflows/{workflow}/run",
            JsonContent.Create(new WorkflowRunHttpRequest { Message = "Episode 12" }),
            tenant);

        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        var runId = await ReadRunIdAsync(started);

        return await ReadWaitingAsync(host, runId, tenant);
    }

    /// <summary>Reads a run's row and, when it waits, the requests it holds.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The run.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The run and its requests; no requests when it does not wait.</returns>
    public static async Task<WaitingRun> ReadWaitingAsync(TraconTestHost host, Guid runId, string? tenant = null)
    {
        // The streamed response ended, so the row is final.
        using var fetched = await Send(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", null, tenant);
        var run = (await fetched.Content.ReadFromJsonAsync<RunRecord>()).ShouldNotBeNull();

        if (run.Status != RunStatus.AwaitingInput)
        {
            return new WaitingRun(run, []);
        }

        using var listed = await Send(host, HttpMethod.Get, $"/tracon/api/workflows/runs/{runId}/requests", null, tenant);
        var requests = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray()
            .Where(static request => !request.TryGetProperty("answered", out var answered) || !answered.GetBoolean())
            .Select(static request => new PendingRequest(
                request.GetProperty("requestId").GetString()!,
                request.GetProperty("form").GetString()!,
                request.GetProperty("prompt").GetString() ?? string.Empty))
            .ToList();

        return new WaitingRun(run, requests);
    }

    /// <summary>Answers a request on the streamed path.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The waiting run.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="answer">The answer's fields.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    public static Task<HttpResponseMessage> RespondAsync(
        TraconTestHost host,
        Guid runId,
        string requestId,
        object answer,
        string? tenant = null)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["requestId"] = JsonSerializer.SerializeToElement(requestId),
        };

        foreach (var property in JsonSerializer.SerializeToElement(answer).EnumerateObject())
        {
            fields[property.Name] = property.Value;
        }

        return Send(host, HttpMethod.Post, $"/tracon/api/workflows/runs/{runId}/respond", JsonContent.Create(fields), tenant);
    }

    /// <summary>Answers the first request of a waiting run.</summary>
    /// <param name="host">The host.</param>
    /// <param name="waiting">The waiting run.</param>
    /// <param name="answer">The answer's fields.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    public static Task<HttpResponseMessage> RespondAsync(TraconTestHost host, WaitingRun waiting, object answer, string? tenant = null)
        => RespondAsync(host, waiting.Run.Id, waiting.RequestId, answer, tenant);

    /// <summary>The run id the first frame of a streamed response names.</summary>
    /// <param name="response">The streamed response.</param>
    /// <returns>The run's id.</returns>
    public static async Task<Guid> ReadRunIdAsync(HttpResponseMessage response)
    {
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    /// <summary>The number of rows in the run table, children included.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The count.</returns>
    public static async Task<int> CountRunsAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 200 })).Count;

    /// <summary>The default tenant of a host that has no tenancy.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The tenant id.</returns>
    public static string TenantId(TraconTestHost host)
        => host.Services.GetRequiredService<ITenantContext>().TenantId;

    private static Task<HttpResponseMessage> Send(
        TraconTestHost host,
        HttpMethod method,
        string uri,
        HttpContent? content,
        string? tenant)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };

        if (tenant is not null)
        {
            request.Headers.Add(WorkflowResponseHarness.TenantHeader, tenant);
        }

        return host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>A workflow run that stopped, and the requests it holds.</summary>
    /// <param name="Run">The run's row.</param>
    /// <param name="Requests">The requests still unanswered.</param>
    internal sealed record WaitingRun(RunRecord Run, IReadOnlyList<PendingRequest> Requests)
    {
        /// <summary>Gets the first request's id.</summary>
        public string RequestId => Requests.Count > 0 ? Requests[0].Id : string.Empty;

        /// <summary>Gets the first request's form.</summary>
        public string Form => Requests.Count > 0 ? Requests[0].Form : string.Empty;

        /// <summary>Gets the first request's prompt.</summary>
        public string Prompt => Requests.Count > 0 ? Requests[0].Prompt : string.Empty;
    }

    /// <summary>One request of a waiting run.</summary>
    /// <param name="Id">The request's id.</param>
    /// <param name="Form">The form the console asks for.</param>
    /// <param name="Prompt">The text shown with it.</param>
    internal sealed record PendingRequest(string Id, string Form, string Prompt);
}
