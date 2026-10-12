using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Stores a run's input messages. The source of replay.
/// </summary>
/// <remarks>
/// <para>
/// A separate interface; a member is <strong>not added</strong> to
/// <see cref="IRunStore"/>. Adding a member to an existing interface is a
/// breaking change after release (the same rationale applies to
/// <c>IRetentionStore</c> and <c>IEvalStore</c>).
/// </para>
/// <para>
/// The input was never persisted anywhere until now: <see cref="RunRecord"/>
/// carries no input, and the <see cref="RunEventType.RunStarted"/> event only
/// carries the <em>text</em> of the first user message, and that
/// text can be truncated. Replay must be faithful; this is why messages are
/// stored in a separate table together with their polymorphic content.
/// </para>
/// <para>
/// <strong>An error from this store does not stop the run.</strong> The write
/// path (<c>RunRecordingAgent</c>) catches and logs the error; observability
/// must not break functionality.
/// </para>
/// <para>
/// <strong>Tenant behavior — EXPECTED tenant, uniformly.</strong>
/// <see cref="SaveAsync"/> reads it from <see cref="RunInputRecord.TenantId"/>;
/// <see cref="GetAsync"/> takes it as a parameter. The ambient tenant is
/// never consulted.
/// </para>
/// </remarks>
public interface IRunInputStore
{
    /// <summary>Saves the input messages.</summary>
    /// <param name="record">The input to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// A second write for the same run is <strong>ignored</strong>: a queued
    /// run can start twice with the same identifier, and the input
    /// must not change.
    /// </remarks>
    ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default);

    /// <summary>Reads the stored input.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record; <see langword="null"/> if it does not exist or belongs to another tenant.</returns>
    ValueTask<RunInputRecord?> GetAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);
}

/// <summary>A run's stored input.</summary>
public sealed record RunInputRecord
{
    /// <summary>The run identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The tenant the run belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The input messages.
    /// </summary>
    /// <remarks>
    /// Carries polymorphic content (<c>TextContent</c>, <c>UriContent</c>,
    /// <c>FunctionResultContent</c> ...). Stored in the <c>json</c> column,
    /// <strong>not</strong> <c>jsonb</c>: <c>jsonb</c> reorders object keys,
    /// and System.Text.Json's <c>$type</c> discriminator must be the object's
    /// first property.
    /// </remarks>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// The run's final parameter values: the values the caller supplied with the
    /// schema defaults applied. <see langword="null"/> means nothing was
    /// recorded (a run from before this field existed, or one started while
    /// input recording was off); an empty map means the run was recorded and
    /// carried no values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two states must stay apart. A replay, a workflow resume and an
    /// approval resume read this to run with the SAME instructions as the source
    /// run; a missing record read as "no values" would let a parameterized
    /// definition run on its defaults and change what the model is told.
    /// </para>
    /// <para>
    /// The values are user content. They are stored in their own column,
    /// protected by <see cref="ProtectedColumn.RunInputParameters"/>.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>The record's creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
