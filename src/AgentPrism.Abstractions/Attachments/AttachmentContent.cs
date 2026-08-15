namespace AgentPrism;

/// <summary>
/// The content of a new attachment to be saved into the store.
/// </summary>
/// <remarks>
/// The content is carried already read into memory, because it stays under the size
/// limit (20 MB by default); the store layer does not deal with streaming. The type
/// itself is not a <c>record</c>: the <see cref="Data"/> field can be large, and the
/// generated <c>ToString</c> and equality comparison must not copy the whole content
/// by accident.
/// </remarks>
public sealed class AttachmentContent
{
    /// <summary>Gets the tenant the attachment will belong to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the session the attachment is bound to. <see langword="null"/> for an upload without a session.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the run that produced the attachment. <see langword="null"/> for a user upload.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Gets the original file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the validated MIME type.</summary>
    public required string MediaType { get; init; }

    /// <summary>Gets the raw content.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Gets the actor that uploaded it, or <see langword="null"/> when it is unknown.</summary>
    public string? CreatedBy { get; init; }
}
