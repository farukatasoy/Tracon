using System.Text.Json;

namespace Tracon;

/// <summary>
/// Reads the stored state generations WITHOUT writing anything, so an
/// operator can ask "can this build still read my data" BEFORE upgrading.
/// </summary>
/// <remarks>
/// <para>
/// This surface is deliberately separate from <see cref="ISessionStore"/> and
/// <see cref="IWorkflowCheckpointStore"/>. Those two are application surfaces
/// scoped to one tenant: they filter every read by the tenant in scope, page
/// their results, and pull the full state payload of every row. A preflight
/// asks a different question — how many rows exist per generation, across
/// every tenant at once — and answering it by paging through the application
/// surface would drag the entire state column across the wire.
/// </para>
/// <para>
/// <strong>Tenant mode — tenant-independent.</strong> Nothing here takes or
/// applies a tenant identifier, and that is the point rather than an
/// omission: an upgrade replaces the process for every tenant at the same
/// moment, so a count that saw only one tenant would under-report the very
/// risk this surface exists to measure. Never expose its results through a
/// per-tenant API: a row count is information about other tenants.
/// </para>
/// <para>
/// <strong>Delivery — no delivery guarantee applies.</strong> Every method is
/// a synchronous read that returns its own result; nothing is queued,
/// retried, or handed to another party. A failed call throws and has changed
/// nothing, so the caller's only recovery is to call again.
/// </para>
/// <para>
/// <strong>Nothing here writes.</strong> Every implementation runs read-only
/// queries and takes no lock; it is safe to run against a live database
/// while the application is serving traffic.
/// </para>
/// <para>
/// The values returned are RAW: this reader reports what is stored, never
/// whether the running build understands it. That comparison belongs to
/// <c>StatePreflight</c> in <c>Tracon.Core</c>, the only layer that knows
/// which generation the current code writes.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered with <c>Replace</c> by
/// whichever SQL provider is active. No implementation is registered when
/// persistence is in memory; resolve it as optional.
/// </para>
/// </remarks>
public interface IStatePreflightReader
{
    /// <summary>Gets the provider name, for example <c>PostgreSQL</c>.</summary>
    string ProviderName { get; }

    /// <summary>Counts the rows of <paramref name="target"/> per stored schema generation.</summary>
    /// <param name="target">The table to count.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One entry per distinct generation. An empty table returns an empty list, never an error.</returns>
    /// <remarks>
    /// A single aggregate query over the whole table; the state payload is
    /// never read. Rows of every tenant are counted, because an upgrade is
    /// not a per-tenant event.
    /// </remarks>
    ValueTask<IReadOnlyList<StateGenerationTally>> TallyAsync(
        StatePreflightTarget target,
        CancellationToken cancellationToken = default);

    /// <summary>Reads at most <paramref name="perGeneration"/> rows of each generation, newest first.</summary>
    /// <param name="target">The table to sample.</param>
    /// <param name="perGeneration">The maximum number of rows to read per generation. <c>0</c> reads nothing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sampled rows, state payload included.</returns>
    /// <remarks>
    /// The sample is taken PER GENERATION rather than "the newest N rows
    /// overall": the newest rows are the ones the running build just wrote,
    /// so they prove nothing. The risk concentrates in the older generations.
    /// </remarks>
    ValueTask<IReadOnlyList<StateSample>> SampleAsync(
        StatePreflightTarget target,
        int perGeneration,
        CancellationToken cancellationToken = default);
}

/// <summary>Names a table a state preflight can read.</summary>
public enum StatePreflightTarget
{
    /// <summary>The <c>sessions</c> table — serialized agent session state.</summary>
    Sessions = 0,

    /// <summary>The <c>workflow_checkpoints</c> table — serialized workflow checkpoint state.</summary>
    WorkflowCheckpoints = 1,
}

/// <summary>The number of stored rows carrying one schema generation.</summary>
public sealed record StateGenerationTally
{
    /// <summary>
    /// The stored Tracon schema generation, or <see langword="null"/> for
    /// rows written before the generation was stamped.
    /// </summary>
    /// <remarks>
    /// Only <c>workflow_checkpoints</c> can produce <see langword="null"/>;
    /// <c>sessions.state_schema_version</c> has been <c>NOT NULL</c> since the
    /// first migration.
    /// </remarks>
    public required int? SchemaGeneration { get; init; }

    /// <summary>The number of rows carrying <see cref="SchemaGeneration"/>.</summary>
    public required long RecordCount { get; init; }
}

/// <summary>One sampled row, read for a decode attempt.</summary>
public sealed record StateSample
{
    /// <summary>The row identifier — a session id, or a checkpoint's row id.</summary>
    public required string Id { get; init; }

    /// <summary>The stored Tracon schema generation, or <see langword="null"/> if never stamped.</summary>
    public required int? SchemaGeneration { get; init; }

    /// <summary>
    /// The Microsoft Agent Framework version the row was written with, or
    /// <see langword="null"/> if it was written before version stamping existed.
    /// </summary>
    public string? MafVersion { get; init; }

    /// <summary>The stored state payload, exactly as persisted.</summary>
    public required JsonElement State { get; init; }
}
