namespace Tracon;

/// <summary>An inbound trigger definition: an external event that starts a queued run.</summary>
/// <remarks>
/// <para>
/// A trigger is identified on the wire by <c>TenantId</c> +
/// <c>Name</c> (<c>POST /api/triggers/{tenantId}/{name}</c>). The
/// caller authenticates with an HMAC signature over the request body, the
/// same <c>WebhookSigner</c> contract the outbound webhooks use, in
/// the reverse direction.
/// </para>
/// <para>
/// <c>SigningSecretConfigurationName</c> is the configuration
/// KEY'S NAME, never the secret's value. The value is resolved from
/// <c>IConfiguration</c> at request time and is never written to a database
/// or a file.
/// </para>
/// </remarks>
public sealed record InboundTrigger
{
    /// <summary>The trigger identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The tenant the trigger belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The trigger name, unique within the tenant.</summary>
    public required string Name { get; init; }

    /// <summary>The kind of target the trigger starts.</summary>
    public required InboundTriggerTargetKind TargetKind { get; init; }

    /// <summary>The agent or workflow name to run.</summary>
    public required string TargetName { get; init; }

    /// <summary>The configuration KEY NAME of the signing secret. Never the value itself.</summary>
    public required string SigningSecretConfigurationName { get; init; }

    /// <summary>How the request body becomes the run's message.</summary>
    public InboundTriggerPayloadMode PayloadMode { get; init; } = InboundTriggerPayloadMode.WholeBody;

    /// <summary>
    /// The dotted path used when <see cref="PayloadMode"/> is
    /// <see cref="InboundTriggerPayloadMode.Path"/>; otherwise <see langword="null"/>.
    /// </summary>
    public string? PayloadPath { get; init; }

    /// <summary>Whether the trigger accepts requests. A disabled trigger rejects every request.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
