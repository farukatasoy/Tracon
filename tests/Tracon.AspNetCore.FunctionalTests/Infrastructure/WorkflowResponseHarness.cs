using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The host, graphs and helpers of the workflow answer tests (phase 208): one graph per
/// response mode, and a single-answer graph whose last step waits for the test.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SingleFlow"/> is registered with the <c>AddWorkflow</c> overload that takes
/// <see cref="WorkflowRegistrationOptions"/>, so every single-answer case also proves that the
/// overload carries the mode into the engine. <see cref="BranchFlow"/> uses the plain overload: its
/// mode is the default.
/// </para>
/// <para>
/// <see cref="SlowFlow"/> parks its last step on <see cref="Gate"/> until the test opens it, so a
/// second answer can arrive while the first one's run is still going.
/// </para>
/// </remarks>
internal sealed class WorkflowResponseHarness
{
    /// <summary>The single-answer graph.</summary>
    public const string SingleFlow = "single-gate";

    /// <summary>The graph that keeps the default, a new run for every answer.</summary>
    public const string BranchFlow = "branch-gate";

    /// <summary>The single-answer graph whose last step waits for <see cref="Gate"/>.</summary>
    public const string SlowFlow = "single-slow";

    /// <summary>The graph that waits for a free-text note; the default mode.</summary>
    public const string NoteFlow = "note-gate";

    /// <summary>The port every graph waits on.</summary>
    public const string Port = "answer-port";

    /// <summary>The tenant header of a host started with tenancy.</summary>
    public const string TenantHeader = "X-Tracon-Tenant";

    /// <summary>Gets the gate the last step of <see cref="SlowFlow"/> waits for.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a signal set when the last step of <see cref="SlowFlow"/> starts waiting.</summary>
    public TaskCompletionSource Parked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts a host with the three graphs.</summary>
    /// <param name="runWorker">Whether the host's own worker leases queued jobs.</param>
    /// <param name="tenancy">Whether the tenant is read from <see cref="TenantHeader"/>.</param>
    /// <param name="resumeInterruptedRuns">
    /// Whether a queued job continues a run its process stopped (one continuation allowed).
    /// </param>
    /// <param name="configureTracon">More Tracon configuration.</param>
    /// <param name="configureServices">More service registrations, made after Tracon's own.</param>
    /// <returns>The host.</returns>
    public Task<TraconTestHost> StartAsync(
        bool runWorker = true,
        bool tenancy = false,
        bool resumeInterruptedRuns = false,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseWorkflows(options =>
                {
                    if (resumeInterruptedRuns)
                    {
                        options.ResumeInterruptedRuns = true;
                        options.MaxResumeAttempts = 1;
                    }
                });

                builder.AddWorkflow(
                    SingleFlow,
                    static _ => Build(SingleFlow, static approved => new ValueTask<string>(approved ? "approved" : "rejected")),
                    static options =>
                    {
                        options.Description = "Takes one answer per request.";
                        options.ResponseMode = WorkflowResponseMode.SingleAnswer;
                    });

                builder.AddWorkflow(
                    BranchFlow,
                    static _ => Build(BranchFlow, static approved => new ValueTask<string>(approved ? "approved" : "rejected")));

                builder.AddWorkflow(NoteFlow, static _ => BuildNote());

                builder.AddWorkflow(
                    SlowFlow,
                    _ => Build(SlowFlow, WaitAsync),
                    static options => options.ResponseMode = WorkflowResponseMode.SingleAnswer);

                if (tenancy)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                services.Configure<TraconSchedulingOptions>(options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(50);
                    options.RunWorker = runWorker;
                });

                configureServices?.Invoke(services);
            });

    /// <summary>Runs a graph until it waits for its answer.</summary>
    /// <param name="host">The host.</param>
    /// <param name="workflow">The graph.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The waiting run and its pending request.</returns>
    public static async Task<(Guid RunId, string RequestId)> RunToAwaitAsync(TraconTestHost host, string workflow, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{workflow}/run")
        {
            Content = JsonContent.Create(new WorkflowRunHttpRequest { Message = "the draft" }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var started = await host.Client.SendAsync(request);

        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        var frames = await SseReader.ReadAllAsync(await started.Content.ReadAsStreamAsync());
        var runId = RunIdOf(frames);

        (await RequestsAsync(host, runId, tenant)).ShouldHaveSingleItem();

        return (runId, (await RequestsAsync(host, runId, tenant))[0]);
    }

    /// <summary>Lists a run's pending request ids.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The run.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The request ids.</returns>
    public static async Task<List<string>> RequestsAsync(TraconTestHost host, Guid runId, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/workflows/runs/{runId}/requests");

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var listed = await host.Client.SendAsync(request);

        return [.. (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().Select(static item => item.GetProperty("requestId").GetString()!)];
    }

    /// <summary>Sends an answer on the streamed path.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The waiting run.</param>
    /// <param name="body">The JSON body.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response, headers read.</returns>
    public static Task<HttpResponseMessage> RespondAsync(
        TraconTestHost host,
        Guid runId,
        object body,
        string? tenant = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/runs/{runId}/respond")
        {
            Content = JsonContent.Create(body),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Reads every frame of a streamed answer.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The frames.</returns>
    public static async Task<List<SseFrame>> FramesAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await ProblemTextAsync(response));

        return [.. await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync())];
    }

    /// <summary>The run id the first frame of a stream announces.</summary>
    /// <param name="frames">The frames.</param>
    /// <returns>The run id.</returns>
    public static Guid RunIdOf(IReadOnlyList<SseFrame> frames)
    {
        frames[0].Event.ShouldBe("run");

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    /// <summary>The workflow runs of the host's store, every tenant.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The runs.</returns>
    public static async Task<List<RunRecord>> WorkflowRunsAsync(TraconTestHost host)
        => [.. (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 500 }))
            .Where(static run => run.Kind == RunKind.Workflow)];

    /// <summary>Reads the answer records of a source run, in the host's default tenant.</summary>
    /// <param name="host">The host.</param>
    /// <param name="sourceRunId">The source run.</param>
    /// <returns>The records.</returns>
    public static async Task<IReadOnlyList<WorkflowResponseRecord>> RecordsAsync(TraconTestHost host, Guid sourceRunId)
        => await host.Services.GetRequiredService<IWorkflowResponseStore>().ListBySourceRunAsync(sourceRunId);

    /// <summary>The <c>workflow.respond</c> entries of the host's audit log.</summary>
    /// <param name="host">The host.</param>
    /// <param name="tenant">The tenant; <see langword="null"/> for the host's default.</param>
    /// <returns>The entries.</returns>
    public static async Task<IReadOnlyList<AuditEntry>> RespondEntriesAsync(TraconTestHost host, string? tenant = null)
        => await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery
        {
            Action = "workflow.respond",
            TenantId = tenant ?? host.Services.GetRequiredService<ITenantContext>().TenantId,
        });

    /// <summary>The body of a failed response, for an assertion message.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The body when the status is not 200; otherwise empty.</returns>
    public static async Task<string> ProblemTextAsync(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.OK ? string.Empty : await response.Content.ReadAsStringAsync();

    private async ValueTask<string> WaitAsync(bool approved)
    {
        Parked.TrySetResult();

        await Gate.Task;

        return approved ? "approved" : "rejected";
    }

    private static Workflow BuildNote()
    {
        var port = RequestPort.Create<string, string>(Port).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Note on: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var finish = ExecutorBindingExtensions.BindAsExecutor<string, string>(static note => $"noted ({note.Length})", id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, finish)
            .WithOutputFrom(finish)
            .WithName(NoteFlow)
            .Build();
    }

    internal static Workflow Build(string name, Func<bool, ValueTask<string>> end)
    {
        var port = RequestPort.Create<string, bool>(Port).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        var finish = ExecutorBindingExtensions.BindAsExecutor(end, id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, finish)
            .WithOutputFrom(finish)
            .WithName(name)
            .Build();
    }
}
