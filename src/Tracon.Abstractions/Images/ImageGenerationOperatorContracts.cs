using System.ComponentModel;

namespace Tracon;

[Description("The request body for an operator image-generation action.")]
internal sealed record ImageGenerationOperatorRequest
{
    [Description("Gets the image prompt.")]
    public string? Prompt { get; init; }

    [Description("Gets the number of images to generate. Defaults to one.")]
    public int? Count { get; init; }

    [Description("Gets the optional image size in WIDTHxHEIGHT form.")]
    public string? Size { get; init; }

    [Description("Gets the session to which generated attachments belong.")]
    public string? SessionId { get; init; }
}

[Description("The result of an operator image-generation action.")]
internal sealed record ImageGenerationOperatorResponse
{
    [Description("Gets the generated image attachments.")]
    public required IReadOnlyList<AttachmentDescriptor> Attachments { get; init; }

    [Description("Gets the observed generation quantity and configured cost.")]
    public required ToolCallUsage Usage { get; init; }
}
