using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The shared host, workflows and job driver of the queued-workflow and recovery
/// tests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two workflows.</strong> <see cref="Recoverable"/> is a stored
/// Sequential definition of two function steps with a <c>tone</c> parameter
/// (default <c>plain</c>): the first step counts its calls, the second counts its
/// calls, records the tone it ran under, and does what the test scripted
/// (<see cref="RecoveryProbe"/>). A checkpoint is written as each super-step ends,
/// so a run stopped inside the second step has a checkpoint after the first.
/// <see cref="Approval"/> is a code graph that waits on a yes/no port, for the
/// respond endpoint.
/// </para>
/// <para>
/// <strong>A crash, in one process.</strong> A killed worker writes nothing more,
/// and its lease runs out. <see cref="StartDoomedAttemptAsync"/> plays that worker:
/// it leases the job by hand with a short lease that nobody renews, runs the real
/// handler from the container, and leaves it hanging in the second step, which
/// ignores the run's token (<see cref="StepBehavior.Hang"/>). The next lease is the
/// survivor's. The hung attempt is released only when the probe is disposed, after
/// every assertion. The real two-process proof (SIGKILL, PostgreSQL) is
/// <c>TwoProcessWorkflowResumeTests</c>.
/// </para>
/// </remarks>
internal static class QueuedWorkflowHarness
{
    /// <summary>The stored two-step workflow.</summary>
    public const string Recoverable = "recoverable";

    /// <summary>The code graph that waits on a yes/no port.</summary>
    public const string Approval = "approval-gate";

    /// <summary>The first function step.</summary>
    public const string FirstStep = "recover-first";

    /// <summary>The second function step.</summary>
    public const string SecondStep = "recover-second";

    /// <summary>The port the approval graph waits on.</summary>
    public const string ApprovalPort = "gate-port";

    /// <summary>Starts a host with both workflows registered.</summary>
    /// <param name="probe">Counts and scripts the steps.</param>
    /// <param name="resumeInterruptedRuns">The recovery switch.</param>
    /// <param name="maxResumeAttempts">The continuation limit.</param>
    /// <param name="runWorker">Whether the host's own worker leases jobs; off when a test drives the attempts.</param>
    /// <param name="configureTracon">More Tracon configuration.</param>
    /// <param name="configureServices">More service registrations.</param>
    /// <returns>The host.</returns>
    public static Task<TraconTestHost> StartAsync(
        RecoveryProbe probe,
        bool resumeInterruptedRuns = true,
        int maxResumeAttempts = 1,
        bool runWorker = true,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                Register(builder, probe, resumeInterruptedRuns, maxResumeAttempts);
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

    /// <summary>Registers the engine, both steps and the approval graph.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="probe">Counts and scripts the steps.</param>
    /// <param name="resumeInterruptedRuns">The recovery switch.</param>
    /// <param name="maxResumeAttempts">The continuation limit.</param>
    public static void Register(ITraconBuilder builder, RecoveryProbe probe, bool resumeInterruptedRuns, int maxResumeAttempts)
    {
        builder.UseWorkflows(options =>
        {
            options.ResumeInterruptedRuns = resumeInterruptedRuns;
            options.MaxResumeAttempts = maxResumeAttempts;
        });

        builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            FirstStep,
            _ => async (messages, context, _) =>
            {
                probe.CountFirst();

                // What a paid step reports (speech, a render); nothing unless a case sets an amount.
                if (probe.FirstAmount > 0m)
                {
                    await context.ReportUsageAsync(RecoveryProbe.Spent(probe.FirstAmount)).ConfigureAwait(false);
                }

                return messages;
            });

        builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            SecondStep,
            _ => async (messages, context, cancellationToken) =>
            {
                // Reported before the step can hang: the process that dies in it has spent this much.
                if (probe.SecondAmount > 0m)
                {
                    await context.ReportUsageAsync(RecoveryProbe.Spent(probe.SecondAmount)).ConfigureAwait(false);
                }

                await probe.SecondAsync(context.GetRunParameters(), cancellationToken).ConfigureAwait(false);

                return messages;
            });

        builder.AddWorkflow(Approval, static _ => BuildApproval(), "Waits for a yes or no.");
    }

    /// <summary>Saves <see cref="Recoverable"/> for the host's default tenant.</summary>
    /// <param name="host">The host.</param>
    /// <returns>A task that completes when the definition is saved.</returns>
    public static async Task SaveRecoverableAsync(TraconTestHost host)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            $"/tracon/api/workflows/{Recoverable}",
            RecoverableDefinition());

        saved.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>The body that saves <see cref="Recoverable"/>.</summary>
    /// <returns>The save request.</returns>
    public static WorkflowSaveRequest RecoverableDefinition() => new()
    {
        Kind = WorkflowKind.Sequential,
        Nodes =
        [
            new WorkflowNodeReference { Name = FirstStep, Kind = WorkflowNodeKind.Function },
            new WorkflowNodeReference { Name = SecondStep, Kind = WorkflowNodeKind.Function },
        ],
        Parameters =
        [
            new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, Required = false, DefaultValue = "plain" },
        ],
    };

    /// <summary>Sends a POST with <c>Prefer: respond-async</c> and, optionally, an <c>Idempotency-Key</c>.</summary>
    /// <param name="host">The host.</param>
    /// <param name="path">The path under <c>/tracon</c>.</param>
    /// <param name="body">The JSON body.</param>
    /// <param name="idempotencyKey">The key, or <see langword="null"/>.</param>
    /// <param name="preferAsync">Whether the preference is sent.</param>
    /// <param name="tenant">The tenant header, or <see langword="null"/>.</param>
    /// <returns>The response.</returns>
    public static async Task<HttpResponseMessage> PostAsync(
        TraconTestHost host,
        string path,
        object body,
        string? idempotencyKey = null,
        bool preferAsync = true,
        string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon{path}") { Content = JsonContent.Create(body) };

        if (preferAsync)
        {
            request.Headers.Add("Prefer", "respond-async");
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        return await host.Client.SendAsync(request);
    }

    /// <summary>Reads the run id of a <c>202</c>.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The run id.</returns>
    public static async Task<Guid> AcceptedRunIdAsync(HttpResponseMessage response)
    {
        // Read as text: a buffered body can be read again as text, the test reads the rest from it.
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted, body);

        using var document = System.Text.Json.JsonDocument.Parse(body);

        return document.RootElement.GetProperty("runId").GetGuid();
    }

    /// <summary>Waits for a job to reach a final status.</summary>
    /// <param name="host">The host.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="tenant">The job's tenant; the host's default when <see langword="null"/>.</param>
    /// <returns>The job as it settled.</returns>
    public static async Task<JobRecord> WaitForJobAsync(TraconTestHost host, Guid jobId, string? tenant = null)
    {
        var store = host.Services.GetRequiredService<IJobStore>();
        var tenantId = tenant ?? TenantOf(host);

        return (await WaitUntil.ValueAsync(
            async () => await store.GetAsync(tenantId, jobId),
            static job => job is { Status: JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled },
            $"job {jobId} to finish"))!;
    }

    /// <summary>Lists every job of the host's default tenant.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The jobs.</returns>
    public static async Task<IReadOnlyList<JobRecord>> JobsAsync(TraconTestHost host)
        => await host.Services.GetRequiredService<IJobStore>().QueryAsync(new JobQuery { TenantId = TenantOf(host), Take = 200 });

    /// <summary>Lists every workflow run of the host's default tenant, oldest first.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The runs.</returns>
    public static async Task<List<RunRecord>> WorkflowRunsAsync(TraconTestHost host)
        => [.. (await host.Services.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { Kind = RunKind.Workflow, OnlyRootRuns = false, Take = 200 }))
            .OrderBy(static run => run.StartedAt)];

    /// <summary>Gets the host's default tenant.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The tenant id.</returns>
    public static string TenantOf(TraconTestHost host) => host.Services.GetRequiredService<ITenantContext>().TenantId;

    /// <summary>
    /// Leases the only ready job by hand with a lease nobody renews, and runs its
    /// handler until it hangs in the second step: a worker that dies there.
    /// </summary>
    /// <param name="host">The host, with its own worker off.</param>
    /// <param name="probe">The probe whose second step hangs.</param>
    /// <param name="lease">The lease the dead worker took.</param>
    /// <param name="waitForCheckpoint">
    /// Whether to wait for the hung run's own first checkpoint. A continuation
    /// resumed from an earlier run's checkpoint has none of its own when it hangs
    /// in the same step.
    /// </param>
    /// <returns>The hung attempt; it ends when the probe is disposed.</returns>
    public static async Task<Task> StartDoomedAttemptAsync(
        TraconTestHost host,
        RecoveryProbe probe,
        TimeSpan lease,
        bool waitForCheckpoint = true)
    {
        var reached = probe.ArmHang(StepBehavior.Hang);
        var job = await LeaseAsync(host, "doomed-worker", lease);
        var attempt = Task.Run(() => RunAttemptAsync(host, job, "doomed-worker", probe.Teardown));

        probe.Track(attempt);

        await Task.WhenAny(reached, attempt).WaitAsync(TimeSpan.FromSeconds(30));

        if (attempt.IsCompleted)
        {
            await attempt;
            throw new InvalidOperationException("The doomed attempt ended before it reached the second step.");
        }

        if (!waitForCheckpoint)
        {
            return attempt;
        }

        // The checkpoint after the first step is written as that super-step
        // ends; wait for it, so the crash leaves exactly what a real one does.
        await WaitUntil.TrueAsync(
            async () => (await host.Services.GetRequiredService<IWorkflowCheckpointStore>()
                .ListByRunAsync(TenantOf(host), probe.RunOfLastSecondStep!.Value)).Count > 0,
            "the first super-step's checkpoint");

        return attempt;
    }

    /// <summary>
    /// Lets the real worker lease and run what is left of a job, and waits for the
    /// job to settle.
    /// </summary>
    /// <param name="host">The host, with its own worker off.</param>
    /// <param name="jobId">The job.</param>
    /// <returns>The job as it settled.</returns>
    /// <remarks>
    /// <see cref="RunAttemptAsync"/> calls the handler straight, so it never meets the
    /// worker's attempt check: a lease past the job's limit is failed by the worker
    /// before any handler runs. A case about the job's LAST lease has to go through
    /// the worker, or it proves a handler path production never reaches. The host's
    /// own worker stays off, so the leases before this one are still the test's.
    /// </remarks>
    public static async Task<JobRecord> FinishWithWorkerAsync(TraconTestHost host, Guid jobId)
    {
        var configured = host.Services.GetRequiredService<IOptionsMonitor<TraconSchedulingOptions>>().CurrentValue;
        var options = new TraconSchedulingOptions
        {
            Enabled = true,
            RunWorker = true,
            MaxConcurrentJobs = configured.MaxConcurrentJobs,
            PollInterval = TimeSpan.FromMilliseconds(50),
            LeaseDuration = configured.LeaseDuration,
            MaxAttempts = configured.MaxAttempts,
        };

        // The map has no setter: copied entry by entry, or a time limit the host was
        // configured with would be silently dropped for the worker's last lease.
        foreach (var (key, limit) in configured.MaxDurationByHandlerKey)
        {
            options.MaxDurationByHandlerKey[key] = limit;
        }

        using var worker = ActivatorUtilities.CreateInstance<JobWorkerBackgroundService>(host.Services, new FixedOptionsMonitor<TraconSchedulingOptions>(options));

        await worker.StartAsync(CancellationToken.None);

        try
        {
            return await WaitForJobAsync(host, jobId);
        }
        finally
        {
            // Bounded: a handler hung in a step that ignores its token (a failing
            // case) would otherwise hold the stop forever. The probe releases it.
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(stop.Token);
        }
    }

    /// <summary>Leases the only ready job, the way a worker does.</summary>
    /// <param name="host">The host.</param>
    /// <param name="owner">The worker's id.</param>
    /// <param name="lease">The lease.</param>
    /// <returns>The leased job.</returns>
    public static async Task<JobRecord> LeaseAsync(TraconTestHost host, string owner, TimeSpan lease)
    {
        var store = host.Services.GetRequiredService<IJobStore>();

        return (await WaitUntil.ValueAsync(
            async () => await store.LeaseAsync(new JobLeaseRequest { Owner = owner, LeaseDuration = lease }),
            static job => job is not null,
            $"a job to lease as '{owner}'"))!;
    }

    /// <summary>
    /// Runs one attempt of a leased job the way the worker does: the job's tenant,
    /// a fresh scope, the handler the container builds for the key, the store's
    /// own item report and cancellation check.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="job">The leased job.</param>
    /// <param name="owner">The worker's id.</param>
    /// <param name="cancellation">The attempt's token source; <see langword="null"/> for none.</param>
    /// <param name="report">Replaces the store's item report, to fail a write; <see langword="null"/> for the store's.</param>
    /// <param name="markRunning">Whether to move the job to Running first; off when the test already did.</param>
    /// <returns>A task that completes when the handler returns.</returns>
    public static async Task RunAttemptAsync(
        TraconTestHost host,
        JobRecord job,
        string owner,
        CancellationTokenSource? cancellation = null,
        Func<JobItemResult, CancellationToken, ValueTask>? report = null,
        bool markRunning = true)
    {
        var services = host.Services;
        var store = services.GetRequiredService<IJobStore>();

        // Opened in this method's own body, before the scope: the worker does the same.
        using var tenant = AmbientTenantScope.Begin(job.TenantId);

        if (markRunning)
        {
            (await store.MarkRunningAsync(job.Id, owner)).ShouldBeTrue($"'{owner}' must hold the lease of job {job.Id}");
        }

        var registry = services.GetRequiredService<JobHandlerRegistry>();
        registry.TryGetHandlerType(job.HandlerKey, out var handlerType).ShouldBeTrue();

        await using var scope = services.CreateAsyncScope();

        var handler = (IJobHandler)scope.ServiceProvider.GetRequiredService(handlerType!);
        var items = await store.ListItemsAsync(job.Id);

        await handler.ExecuteAsync(
            new JobContext
            {
                Job = job,
                Items = items,
                ReportItemAsync = report ?? ((result, ct) => store.ReportItemAsync(result, ct)),
                IsCancelledAsync = async ct =>
                {
                    var current = await store.GetAsync(job.TenantId, job.Id, ct);
                    return current is null || current.Status == JobStatus.Cancelled;
                },
            },
            cancellation?.Token ?? CancellationToken.None);
    }

    private static Workflow BuildApproval()
    {
        var port = RequestPort.Create<string, bool>(ApprovalPort).BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "gate-start");

        var end = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "approved" : "rejected",
            id: "gate-end");

        return new WorkflowBuilder(start)
            .AddEdge(start, port)
            .AddEdge(port, end)
            .WithOutputFrom(end)
            .WithName(Approval)
            .Build();
    }
}

/// <summary>What the second step does on one call.</summary>
internal enum StepBehavior
{
    /// <summary>Returns at once.</summary>
    Pass,

    /// <summary>Hangs and ignores the run's token, like a step of a process that died.</summary>
    Hang,

    /// <summary>Hangs until the run's token is cancelled, like a step of a process that stops gracefully.</summary>
    HangUntilCancelled,
}

/// <summary>Counts and scripts the two steps of <see cref="QueuedWorkflowHarness.Recoverable"/>.</summary>
internal sealed class RecoveryProbe : IAsyncDisposable
{
    private readonly ConcurrentQueue<(StepBehavior Behavior, TaskCompletionSource Reached)> _script = new();
    private readonly ConcurrentQueue<string?> _tones = new();
    private readonly ConcurrentQueue<decimal> _costAtSecond = new();
    private readonly ConcurrentBag<Task> _attempts = [];
    private int _first;
    private int _second;

    /// <summary>Gets the token that releases a hung step; cancelled when the probe is disposed.</summary>
    public CancellationTokenSource Teardown { get; } = new();

    /// <summary>Gets how many times the first step ran.</summary>
    public int FirstCalls => Volatile.Read(ref _first);

    /// <summary>Gets how many times the second step ran.</summary>
    public int SecondCalls => Volatile.Read(ref _second);

    /// <summary>Gets the tone each call of the second step ran under, in order.</summary>
    public IReadOnlyList<string?> Tones => [.. _tones];

    /// <summary>Gets the run the second step last ran in.</summary>
    public Guid? RunOfLastSecondStep { get; private set; }

    /// <summary>Gets or sets the amount the first step reports; none by default.</summary>
    public decimal FirstAmount { get; set; }

    /// <summary>Gets or sets the amount the second step reports before it runs; none by default.</summary>
    public decimal SecondAmount { get; set; }

    /// <summary>Gets what the tree's budget held each time the second step started, after the step reported, in order.</summary>
    public IReadOnlyList<decimal> CostAtSecond => [.. _costAtSecond];

    /// <summary>An item a step reports: characters with an amount in USD.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The item.</returns>
    public static ToolCallUsage Spent(decimal amount)
        => new()
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = 100m,
            Cost = amount,
            Currency = "USD",
            Provider = "elevenlabs",
            Model = "eleven_multilingual_v2",
        };

    /// <summary>Scripts the next call of the second step.</summary>
    /// <param name="behavior">What the call does.</param>
    /// <returns>A task that completes when that call starts.</returns>
    public Task ArmHang(StepBehavior behavior)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _script.Enqueue((behavior, reached));

        return reached.Task;
    }

    /// <summary>Keeps a hung attempt, so disposing the probe releases it and waits for it.</summary>
    /// <param name="attempt">The attempt.</param>
    public void Track(Task attempt) => _attempts.Add(attempt);

    /// <summary>Counts a call of the first step.</summary>
    public void CountFirst() => Interlocked.Increment(ref _first);

    /// <summary>Runs one call of the second step.</summary>
    /// <param name="parameters">The run's parameter values.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <returns>A task that completes when the call returns.</returns>
    public async Task SecondAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _second);
        _tones.Enqueue(parameters.TryGetValue("tone", out var tone) ? tone : null);
        RunOfLastSecondStep = TraconRunContext.Current?.RootRunId ?? TraconRunContext.Current?.RunId;

        if (TraconRunContext.Current?.Budget is { } budget)
        {
            _costAtSecond.Enqueue(budget.ConsumedCost);
        }

        if (!_script.TryDequeue(out var next))
        {
            return;
        }

        next.Reached.TrySetResult();

        switch (next.Behavior)
        {
            case StepBehavior.Hang:
                await Task.Delay(Timeout.Infinite, Teardown.Token).ConfigureAwait(false); // delay: simulated
                break;

            case StepBehavior.HangUntilCancelled:
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated
                break;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dispose it BEFORE the host (declare it after the host with <c>await using</c>):
    /// a released attempt writes its last lines while the stores are still up.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await Teardown.CancelAsync();

        foreach (var attempt in _attempts)
        {
            try
            {
                await attempt.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception exception) when (exception is OperationCanceledException or TraconException or TimeoutException)
            {
                // A hung attempt ends by cancellation; what it does then is no
                // longer under test.
            }
        }

        Teardown.Dispose();
    }
}

/// <summary>Options that never change, for a worker the test builds itself.</summary>
/// <typeparam name="T">The options type.</typeparam>
internal sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    /// <inheritdoc />
    public T CurrentValue => value;

    /// <inheritdoc />
    public T Get(string? name) => value;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>An attribution the test sets, the same on every request and in the worker.</summary>
internal sealed class SettableAttribution : IRunAttributionContext
{
    /// <inheritdoc />
    public string? UserId { get; set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string>? Labels { get; set; }
}
