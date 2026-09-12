using System.Text.Json;

namespace Tracon;

/// <summary>
/// Represents a single checkpoint of a workflow run.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="State"/> is an <strong>opaque</strong> payload. Its content belongs to Microsoft Agent Framework and is polymorphic: it carries a <c>$type</c> discriminator, and that discriminator must be the <em>first</em> property of the object it appears in. Measured: a 7.5 KB checkpoint really does have the <c>{"$type":0,...}</c> discriminator first.
/// </para>
/// <para>
/// For this reason the value is stored in a PostgreSQL <c>json</c> column, not a
/// <c>jsonb</c> column: <c>jsonb</c> reorders keys and would move the discriminator out
/// of first place.
/// </para>
/// </remarks>
public sealed record WorkflowCheckpointRecord
{
    /// <summary>Gets the record identifier. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant the record belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the identifier of the run session. Microsoft Agent Framework
    /// groups checkpoints under this value.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the checkpoint identifier. The value belongs to Tracon, not
    /// to Microsoft Agent Framework: <c>CreateAsync</c> generates it and
    /// hands it back to MAF.
    /// </summary>
    public required string CheckpointId { get; init; }

    /// <summary>Gets the identifier of the previous checkpoint. <see langword="null"/> for the first checkpoint.</summary>
    public string? ParentCheckpointId { get; init; }

    /// <summary>
    /// Gets the identifier of the run that produced this checkpoint.
    /// <see langword="null"/> if no run was in progress when the checkpoint
    /// was written.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Gets the creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the opaque run state.</summary>
    public required JsonElement State { get; init; }

    /// <summary>Gets the Tracon schema generation that wrote <c>State</c>.</summary>
    /// <remarks>
    /// <see langword="null"/> means the row was written before this field
    /// existed — unlike <c>SessionRecord.StateSchemaVersion</c>,
    /// checkpoints had no envelope stamp before this field was added.
    /// </remarks>
    public int? StateSchemaVersion { get; init; }

    /// <summary>
    /// Gets the Microsoft Agent Framework package version that produced
    /// <c>State</c>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means the row was written before this field
    /// existed. Tracon does not promise that a checkpoint written by one
    /// Microsoft Agent Framework version can be resumed by a different one;
    /// this value lets a failed resume report exactly which version wrote
    /// the state instead of guessing.
    /// </remarks>
    public string? StateMafVersion { get; init; }
}
