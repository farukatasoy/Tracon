namespace AgentPrism;

/// <summary>The request body for an operator image-generation action.</summary>
public sealed record ImageGenerationOperatorRequest
{
    /// <summary>Gets the image prompt.</summary>
    public string? Prompt { get; init; }

    /// <summary>Gets the number of images to generate. Defaults to one.</summary>
    public int? Count { get; init; }

    /// <summary>Gets the optional image size in WIDTHxHEIGHT form.</summary>
    public string? Size { get; init; }

    /// <summary>Gets the session to which generated attachments belong.</summary>
    public string? SessionId { get; init; }
}

/// <summary>The result of an operator image-generation action.</summary>
public sealed record ImageGenerationOperatorResponse
{
    /// <summary>Gets the generated image attachments.</summary>
    public required IReadOnlyList<AttachmentDescriptor> Attachments { get; init; }

    /// <summary>Gets the observed generation quantity and configured cost.</summary>
    public required ToolCallUsage Usage { get; init; }
}
