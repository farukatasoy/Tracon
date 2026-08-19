namespace AgentPrism;

/// <summary>The response describing an inbound trigger definition.</summary>
/// <remarks>🚨 Carries no signing secret value, only the configuration key's NAME (K-059).</remarks>
public sealed record InboundTriggerResponse
{
    /// <summary>Gets the trigger name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the kind of target the trigger starts.</summary>
    public required InboundTriggerTargetKind TargetKind { get; init; }

    /// <summary>Gets the agent or workflow name the trigger starts.</summary>
    public required string TargetName { get; init; }

    /// <summary>Gets the configuration key name the signing secret is read from.</summary>
    public required string SigningSecretConfigurationName { get; init; }

    /// <summary>Gets whether <see cref="SigningSecretConfigurationName"/> currently resolves to a value.</summary>
    public required bool Resolved { get; init; }

    /// <summary>Gets how the request body becomes the run's message.</summary>
    public required InboundTriggerPayloadMode PayloadMode { get; init; }

    /// <summary>Gets the dotted path used when <see cref="PayloadMode"/> is <see cref="InboundTriggerPayloadMode.Path"/>.</summary>
    public string? PayloadPath { get; init; }

    /// <summary>Gets whether the trigger accepts requests.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets the creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The request body for creating or replacing an inbound trigger.</summary>
public sealed record InboundTriggerSaveRequest
{
    /// <summary>Gets the kind of target the trigger starts. Default <see cref="InboundTriggerTargetKind.Agent"/>.</summary>
    public InboundTriggerTargetKind TargetKind { get; init; } = InboundTriggerTargetKind.Agent;

    /// <summary>Gets the agent or workflow name to run.</summary>
    public string? TargetName { get; init; }

    /// <summary>Gets the configuration key name the signing secret is read from. Never a value.</summary>
    public string? SigningSecretConfigurationName { get; init; }

    /// <summary>Gets how the request body becomes the run's message. Default <see cref="InboundTriggerPayloadMode.WholeBody"/>.</summary>
    public InboundTriggerPayloadMode PayloadMode { get; init; } = InboundTriggerPayloadMode.WholeBody;

    /// <summary>Gets the dotted path used when <see cref="PayloadMode"/> is <see cref="InboundTriggerPayloadMode.Path"/>.</summary>
    public string? PayloadPath { get; init; }

    /// <summary>Gets whether the trigger accepts requests. Default <see langword="true"/>.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>The response for a successfully accepted inbound trigger event.</summary>
public sealed record InboundTriggerAcceptedResponse
{
    /// <summary>
    /// Gets the identifier of the queued run, when the trigger targets an
    /// agent. <see langword="null"/> for a workflow target — a workflow job
    /// is not tied to a single run id until it is picked up from the queue.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Gets the identifier of the queued job.</summary>
    public required Guid JobId { get; init; }

    /// <summary>Gets the address to poll for the outcome. Same as the <c>Location</c> header.</summary>
    public required string Location { get; init; }

    /// <summary>Gets the address of the run's event stream. <see langword="null"/> for a workflow target.</summary>
    public string? EventsLocation { get; init; }
}
