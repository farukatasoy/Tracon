using System.Text.Json;

namespace Tracon;

/// <summary>
/// Queues a job for a registered <see cref="IJobHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// The supported way for consumer code to create work. <see cref="IJobStore"/>
/// stays the persistence surface: it takes a fully formed
/// <see cref="JobRecord"/>, so the caller has to invent the identifier, the
/// status and the timestamps, and it accepts <strong>any</strong> handler key,
/// including one nobody registered — such a job only fails later, in the
/// worker. This interface closes both gaps.
/// </para>
/// <para>
/// It performs no authorization of its own: the caller is already inside the
/// process. The HTTP surface has its own, narrower gate
/// (<see cref="TraconSchedulingOptions.HttpSchedulableHandlerKeys"/>).
/// </para>
/// <para>
/// <strong>Lifetime:</strong> registered as a <c>singleton</c> and safe to
/// call from any thread; it holds no per-call state.
/// </para>
/// <para>
/// <strong>Tenant:</strong> takes an <c>expected tenant</c> —
/// <see cref="JobRequest.TenantId"/> is required and is the tenant the job is
/// queued for. The ambient tenant is not consulted; a caller that wants it
/// passes it explicitly.
/// </para>
/// <para>
/// <strong>Delivery:</strong> queueing itself is exactly one row — the method
/// either returns a queued <see cref="JobRecord"/> or throws, and it never
/// retries on its own. What happens afterwards is the queue's contract, and
/// that is <c>at-least-once</c>: the handler may run more than once for the
/// job this call created (see <see cref="IJobHandler.ExecuteAsync"/>).
/// </para>
/// <para>
/// <strong>Attribution:</strong> a <see cref="JobHandlerKeys.Workflow"/> or
/// <see cref="JobHandlerKeys.AgentBatch"/> job carries the user and labels
/// <see cref="IRunAttributionContext"/> resolves at the time of this call: the
/// current request, or an open <see cref="AmbientRunAttributionScope"/> with
/// the built-in context. The worker that runs the job sees neither, so they are
/// written into the stored payload as the envelope
/// <c>{"items": [...], "userId": "...", "labels": {...}}</c>, and every run the
/// job starts is attributed to them. A payload that is already such an
/// envelope keeps its own attribution. No other handler key is changed.
/// </para>
/// </remarks>
public interface IJobDispatcher
{
    /// <summary>Validates the request and queues the job.</summary>
    /// <param name="request">What to run, for whom, and with what input.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The queued job record, with its identifier and counters filled in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <see cref="JobRequest.HandlerKey"/> is not registered, the request's
    /// tenant, target or lane is not valid, <see cref="JobRequest.MaxDuration"/> is
    /// outside one millisecond to about 49.7 days, or a workflow or agent batch
    /// payload is an attribution envelope that is malformed or breaks a
    /// <see cref="RunLabels"/> limit.
    /// </exception>
    ValueTask<JobRecord> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The description of one job to queue through <see cref="IJobDispatcher"/>.</summary>
public sealed record JobRequest
{
    /// <summary>The tenant the job belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The key of the handler to run. Must be registered with
    /// <c>AddJobHandler&lt;T&gt;(key)</c>, or be one of
    /// <see cref="JobHandlerKeys.BuiltIn"/>.
    /// </summary>
    public required string HandlerKey { get; init; }

    /// <summary>
    /// The handler's target — the agent or workflow name for the built-in
    /// keys. A handler that needs no target passes an empty string.
    /// </summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// The lane the job is queued under. Left at <see cref="JobLanes.Default"/>,
    /// <see cref="TraconSchedulingOptions.LaneByHandlerKey"/> may still
    /// route it elsewhere.
    /// </summary>
    public string Lane { get; init; } = JobLanes.Default;

    /// <summary>The input set or parameters, interpreted by the handler.</summary>
    /// <remarks>
    /// <para>
    /// An unset payload is queued as the empty JSON array, never as
    /// <see cref="JsonValueKind.Undefined"/> or JSON <c>null</c>: an undefined
    /// value cannot be written as JSON, and SQL Server rejects a <c>null</c>
    /// literal in a JSON column. Every reader treats the empty array as "no payload".
    /// </para>
    /// <para>
    /// For <see cref="JobHandlerKeys.Workflow"/> and <see cref="JobHandlerKeys.AgentBatch"/>,
    /// a JSON object whose members are all among <c>items</c>, <c>userId</c> and
    /// <c>labels</c> is an attribution envelope: its items are the <c>items</c>
    /// array, and its runs are attributed to that user and those labels.
    /// </para>
    /// </remarks>
    public JsonElement Payload
    {
        get;
        init => field = FreeFormJson.OrEmpty(value);
    } = FreeFormJson.Empty;

    /// <summary>
    /// The job's items. Left empty, the items are extracted from
    /// <see cref="Payload"/> the same way a schedule's payload is.
    /// </summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>
    /// This job's own attempt limit. <see langword="null"/> applies
    /// <see cref="TraconSchedulingOptions.MaxAttempts"/>.
    /// </summary>
    public int? MaxAttempts { get; init; }

    /// <summary>
    /// The longest time one attempt of this job may run. <see langword="null"/>
    /// applies <see cref="TraconSchedulingOptions.MaxDurationByHandlerKey"/>, or no
    /// limit for a key the map does not list. See <see cref="JobRecord.MaxDuration"/>.
    /// </summary>
    /// <remarks>Must be from one millisecond to about 49.7 days.</remarks>
    public TimeSpan? MaxDuration { get; init; }

    /// <summary>
    /// The earliest time the job may run (UTC). <see langword="null"/> means
    /// as soon as a worker leases it.
    /// </summary>
    public DateTimeOffset? ScheduledFor { get; init; }
}
