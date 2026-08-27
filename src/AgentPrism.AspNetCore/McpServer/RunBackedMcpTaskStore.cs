using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

namespace AgentPrism;

/// <summary>
/// Implements the MCP Tasks extension's <see cref="IMcpTaskStore"/> on top of
/// AgentPrism's existing <see cref="IRunStore"/>: an MCP task id IS a run id.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why no new table.</strong> A run row is already durable, already
/// tenant-filtered by <see cref="IRunStore"/>'s own ambient-tenant behavior, and
/// already visible across instances through the shared database — everything
/// SEP-2663 asks of a task store, for free, as long as the task id and the run
/// id are the same value.
/// </para>
/// <para>
/// <strong>Deferred attachment.</strong> <c>.WithTasks(builder, store)</c> needs a
/// concrete instance before the DI container is built, so this type is
/// constructed empty at <c>UseMcpServer</c> registration time and wired to the
/// real, built <see cref="IServiceProvider"/> in <c>MapAgentPrismMcpServer</c> —
/// the SAME pattern <c>ExternalAgentProxy</c> uses for the A2A surface.
/// </para>
/// <para>
/// <strong>Same-instance cache.</strong> <see cref="SetCompletedAsync"/> and
/// <see cref="SetFailedAsync"/> receive the EXACT payload the SDK's background
/// execution produced (already serialized by the SDK from whatever
/// <see cref="CatalogToolCallHandler"/> returned). That payload is cached
/// in-memory, keyed by task id, and preferred by <see cref="GetTaskAsync"/>
/// whenever present — it is byte-identical to what a synchronous call would
/// have returned, including the exact approval-rejection text. A poll that lands
/// on a DIFFERENT instance (or in the narrow window before this instance's
/// write lands) falls back to reconstructing the same shape from
/// <see cref="IRunStore"/> alone — see <see cref="McpTaskStatusMapping"/>.
/// </para>
/// <para>
/// <strong>Elicitation / MRTR.</strong> Out of scope for this store — it covers
/// only the Tasks slice; <see cref="SetInputRequestsAsync"/> and
/// <see cref="ResolveInputRequestsAsync"/> are left empty on purpose, and
/// <see cref="InputResponseReceived"/> is never raised. AgentPrism's
/// <c>ExecutionModeSelector</c> never asks for <see cref="McpTaskStatus.InputRequired"/>,
/// so the SDK never calls these.
/// </para>
/// </remarks>
internal sealed class RunBackedMcpTaskStore : IMcpTaskStore
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (McpTaskStatus Status, JsonElement Payload)> _cache = new(StringComparer.Ordinal);

    // 🚨 The owning tenant, remembered from CreateTaskAsync. Every OTHER
    // method here can run under the WRONG ambient tenant: SetCancelledAsync
    // is called once from tasks/cancel's own caller (which may be a
    // DIFFERENT tenant reading a taskId it does not own — its ambient must
    // NOT be trusted for a write) and once more from the SDK's own
    // post-cancellation catch block, which by then runs OUTSIDE
    // CatalogToolCallHandler's tenant scope entirely (the exception already
    // unwound past it). Housekeeping writes (closing an orphaned row) use
    // THIS map plus AmbientTenantScope instead of trusting whichever tenant
    // happens to be ambient at the call site — GetTaskAsync is the one place
    // that still must, since that is the actual isolation boundary for READS.
    private readonly ConcurrentDictionary<string, string> _taskTenants = new(StringComparer.Ordinal);

    // When each task was created, for the opportunistic sweep below. Both
    // maps above are otherwise never evicted: a long-lived server would
    // accumulate one entry (the full result payload, for _cache) per
    // task-mode call forever.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _createdAt = new(StringComparer.Ordinal);

    private long _lastSweepTicks;

    private IServiceProvider? _services;

#pragma warning disable CS0067 // Never raised: elicitation/MRTR is out of scope (§117.7).
    public event Action<InputResponseReceivedEventArgs>? InputResponseReceived;
#pragma warning restore CS0067

    /// <summary>Wires this store to the built application's root service provider.</summary>
    /// <param name="services">The application's root service provider.</param>
    internal void AttachServices(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public async Task<McpTaskInfo> CreateTaskAsync(CancellationToken cancellationToken = default)
    {
        var provisioning = McpTaskRunAmbient.Current ?? throw new InvalidOperationException(
            "MCP task creation requires AgentPrism's task-provisioning filter, which UseMcpServer() " +
            "registers automatically. RunBackedMcpTaskStore cannot be used outside that pipeline.");

        var services = RequireServices();
        var runStore = services.GetRequiredService<IRunStore>();
        var options = CurrentOptions(services);
        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = clock.GetUtcNow();

        SweepExpired(now, options.TaskTimeToLive);

        // Per SEP-2663 §306 (IMcpTaskStore.CreateTaskAsync's own contract), the
        // task must be durably created before this method returns: a
        // tasks/get for this id must resolve even from a different instance.
        await runStore.StartRunAsync(
            new RunStartInfo
            {
                RunId = provisioning.RunId,
                AgentName = provisioning.AgentName is { Length: > 0 } ? provisioning.AgentName : "(unresolved)",
                Status = RunStatus.Queued,
                StartedAt = now,
                TenantId = provisioning.TenantId,
            },
            cancellationToken).ConfigureAwait(false);

        _taskTenants[provisioning.RunId.ToString()] = provisioning.TenantId;
        _createdAt[provisioning.RunId.ToString()] = now;

        return new McpTaskInfo(
            provisioning.RunId.ToString(),
            McpTaskStatus.Working,
            now,
            now,
            options.TaskTimeToLive,
            (long)options.TaskPollInterval.TotalMilliseconds);
    }

    /// <inheritdoc />
    public async Task<McpTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(taskId, out var runId))
        {
            return null;
        }

        var services = RequireServices();
        var runStore = services.GetRequiredService<IRunStore>();
        var run = await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        (McpTaskStatus Status, JsonElement Payload)? cachedOutcome =
            _cache.TryGetValue(taskId, out var cached) ? cached : null;

        return await McpTaskStatusMapping
            .ToTaskInfoAsync(run, cachedOutcome, runStore, CurrentOptions(services), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetCompletedAsync(string taskId, JsonElement result, CancellationToken cancellationToken = default)
    {
        _cache[taskId] = (McpTaskStatus.Completed, result);

        // Usually a no-op: when CatalogToolCallHandler actually called
        // agent.RunAsync, RunRecordingAgent's own completion write already
        // closed the run before this method runs. But CatalogToolCallHandler
        // has THREE early returns before agent.RunAsync (unknown/unexposed
        // tool, empty message, agent not found in the catalog) that produce a
        // normal (if IsError) CallToolResult -- the SDK routes that through
        // THIS method too, and for those the placeholder row CreateTaskAsync
        // opened was never touched by anything else. Without this, such a
        // task is stuck reporting Working forever.
        if (Guid.TryParse(taskId, out var runId))
        {
            await CloseIfStillQueuedAsync(runId, RunStatus.Completed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SetFailedAsync(string taskId, JsonElement error, CancellationToken cancellationToken = default)
    {
        _cache[taskId] = (McpTaskStatus.Failed, error);

        // Reached only when the tool pipeline never got as far as
        // CatalogToolCallHandler at all (a failure in an earlier filter, for
        // example) -- CatalogToolCallHandler itself never throws except
        // OperationCanceledException, so its OWN failures already close the
        // run through RunRecordingAgent. Without this, such a run would be
        // stuck at Queued forever: nothing else will ever close it.
        if (Guid.TryParse(taskId, out var runId))
        {
            await CloseIfStillQueuedAsync(runId, RunStatus.Failed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetCancelledAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(taskId, out var runId))
        {
            return false;
        }

        var services = RequireServices();
        var runStore = services.GetRequiredService<IRunStore>();

        using var tenantScope = _taskTenants.TryGetValue(taskId, out var owningTenant)
            ? AmbientTenantScope.Begin(owningTenant)
            : null;

        var run = await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return false;
        }

        if (run.Status == RunStatus.Queued)
        {
            // Never actually started: the SDK schedules the background
            // execution on the thread pool, and this can race ahead of it.
            // Nothing else will ever close this row, so this ties the same
            // knot RunEndpoints.CancelRunAsync ties for a queued HTTP run.
            var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;

            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Canceled,
                    CompletedAt = clock.GetUtcNow(),
                    TenantId = run.TenantId,
                },
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        // Running: the SDK cancels the SAME CancellationToken already flowing
        // into CatalogToolCallHandler's agent.RunAsync call (it is the token
        // this very method receives, sourced from the SDK's per-task
        // CancellationTokenSource). RunRecordingAgent links it into its own
        // registered source (Phase 32) and closes the run itself; writing
        // here too would race it.
        return run.Status == RunStatus.Running;
    }

    /// <inheritdoc />
    /// <remarks>Elicitation/MRTR is out of scope for this store — never called.</remarks>
    public Task ResolveInputRequestsAsync(
        string taskId, IDictionary<string, InputResponse> inputResponses, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>Elicitation/MRTR is out of scope for this store — never called.</remarks>
    public Task SetInputRequestsAsync(
        string taskId, IDictionary<string, InputRequest> inputRequests, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private async Task CloseIfStillQueuedAsync(Guid runId, RunStatus terminalStatus, CancellationToken cancellationToken)
    {
        var services = RequireServices();
        var runStore = services.GetRequiredService<IRunStore>();

        using var tenantScope = _taskTenants.TryGetValue(runId.ToString(), out var owningTenant)
            ? AmbientTenantScope.Begin(owningTenant)
            : null;

        var run = await runStore.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);

        if (run is null || run.Status != RunStatus.Queued)
        {
            return;
        }

        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;

        await runStore.CompleteRunAsync(
            new RunCompletion
            {
                RunId = runId,
                Status = terminalStatus,
                CompletedAt = clock.GetUtcNow(),
                TenantId = run.TenantId,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reclaims the same-instance caches for tasks older than the current
    /// time-to-live. Throttled to once per <see cref="SweepInterval"/>, same
    /// shape as the SDK's own <c>InMemoryMcpTaskStore.SweepExpired</c>. This
    /// never touches <see cref="IRunStore"/> — the run row's own lifetime is
    /// governed by the retention policy regardless of what happens here; a
    /// swept entry just falls back to <see cref="McpTaskStatusMapping"/>'s
    /// reconstruction path on its next poll, the same path a different
    /// instance's poll already takes.
    /// </summary>
    private void SweepExpired(DateTimeOffset now, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        var nowTicks = now.UtcTicks;
        var lastTicks = Interlocked.Read(ref _lastSweepTicks);

        if (nowTicks - lastTicks < SweepInterval.Ticks ||
            Interlocked.CompareExchange(ref _lastSweepTicks, nowTicks, lastTicks) != lastTicks)
        {
            return;
        }

        foreach (var (taskId, createdAt) in _createdAt)
        {
            if (now - createdAt < ttl)
            {
                continue;
            }

            _createdAt.TryRemove(taskId, out _);
            _taskTenants.TryRemove(taskId, out _);
            _cache.TryRemove(taskId, out _);
        }
    }

    private static AgentPrismMcpServerOptions CurrentOptions(IServiceProvider services)
        => services.GetRequiredService<IOptionsMonitor<AgentPrismMcpServerOptions>>().CurrentValue;

    private IServiceProvider RequireServices()
        => _services ?? throw new InvalidOperationException(
            "RunBackedMcpTaskStore is not attached to a service provider. Call MapAgentPrismMcpServer() " +
            "before serving MCP requests.");
}
