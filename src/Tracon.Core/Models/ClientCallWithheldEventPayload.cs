namespace Tracon;

/// <summary>The <see cref="RunEventCustomTypes.ClientCallWithheld"/> event payload.</summary>
/// <remarks>Names the call and the tool. The arguments are the model's text and are never recorded here.</remarks>
internal sealed record ClientCallWithheldEventPayload
{
    /// <summary>Gets the identifier the model gave the call; <see langword="null"/> when the provider gave none.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Gets the name of the client tool the call asked for.</summary>
    public required string ToolName { get; init; }
}
