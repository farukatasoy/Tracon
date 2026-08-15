using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Represents a single checkpoint of a workflow run.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <see cref="State"/> is an <strong>opaque</strong> payload. Its content
/// belongs to Microsoft Agent Framework and is polymorphic: it carries a
/// <c>$type</c> discriminator, and that discriminator must be the
/// <em>first</em> property of the object it appears in. Measured (Phase 15):
/// a 7.5 KB checkpoint really does have the <c>{"$type":0,...}</c>
/// discriminator first.
/// </para>
/// <para>
/// For this reason the value is stored in a PostgreSQL <c>json</c> column,
/// not a <c>jsonb</c> column: <c>jsonb</c> reorders keys and would move the
/// discriminator out of first place. Decision K-027.
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
    /// Gets the checkpoint identifier. The value belongs to AgentPrism, not
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
}
