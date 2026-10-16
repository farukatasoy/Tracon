using System.ComponentModel;

namespace Tracon;

/// <remarks>Carries no signing secret value, only the configuration key's NAME.</remarks>
[Description("The response describing an inbound trigger definition.")]
internal sealed record InboundTriggerResponse
{
    [Description("Gets the trigger name.")]
    public required string Name { get; init; }

    [Description("Gets the kind of target the trigger starts.")]
    public required InboundTriggerTargetKind TargetKind { get; init; }

    [Description("Gets the agent or workflow name the trigger starts.")]
    public required string TargetName { get; init; }

    [Description("Gets the configuration key name the signing secret is read from.")]
    public required string SigningSecretConfigurationName { get; init; }

    [Description("Gets whether `SigningSecretConfigurationName` currently resolves to a value.")]
    public required bool Resolved { get; init; }

    [Description("Gets how the request body becomes the run's message.")]
    public required InboundTriggerPayloadMode PayloadMode { get; init; }

    [Description("Gets the dotted path used when PayloadMode is InboundTriggerPayloadMode.Path.")]
    public string? PayloadPath { get; init; }

    [Description("Gets whether the trigger accepts requests.")]
    public required bool Enabled { get; init; }

    [Description("Gets the creation time (UTC).")]
    public required DateTimeOffset CreatedAt { get; init; }

    [Description("Gets the last-updated time (UTC).")]
    public required DateTimeOffset UpdatedAt { get; init; }
}

[Description("The request body for creating or replacing an inbound trigger.")]
internal sealed record InboundTriggerSaveRequest
{
    [Description("Gets the kind of target the trigger starts. Default InboundTriggerTargetKind.Agent.")]
    public InboundTriggerTargetKind TargetKind { get; init; } = InboundTriggerTargetKind.Agent;

    [Description("Gets the agent or workflow name to run.")]
    public string? TargetName { get; init; }

    [Description("Gets the configuration key name the signing secret is read from. Never a value.")]
    public string? SigningSecretConfigurationName { get; init; }

    [Description("Gets how the request body becomes the run's message. Default InboundTriggerPayloadMode.WholeBody.")]
    public InboundTriggerPayloadMode PayloadMode { get; init; } = InboundTriggerPayloadMode.WholeBody;

    [Description("Gets the dotted path used when PayloadMode is InboundTriggerPayloadMode.Path.")]
    public string? PayloadPath { get; init; }

    [Description("Gets whether the trigger accepts requests. Default `true`.")]
    public bool Enabled { get; init; } = true;
}

[Description("The response for a successfully accepted inbound trigger event.")]
internal sealed record InboundTriggerAcceptedResponse
{
    [Description(
        "Gets the identifier of the queued run, when the trigger targets an\n" +
        "agent. `null` for a workflow target — a workflow job\n" +
        "is not tied to a single run id until it is picked up from the queue.")]
    public Guid? RunId { get; init; }

    [Description("Gets the identifier of the queued job.")]
    public required Guid JobId { get; init; }

    [Description("Gets the address to poll for the outcome. Same as the `Location` header.")]
    public required string Location { get; init; }

    [Description("Gets the address of the run's event stream. `null` for a workflow target.")]
    public string? EventsLocation { get; init; }
}
