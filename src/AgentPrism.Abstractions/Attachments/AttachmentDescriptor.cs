namespace AgentPrism;

/// <summary>
/// The metadata of an uploaded attachment.
/// </summary>
/// <remarks>
/// The binary content is NOT CARRIED here. It is read separately with
/// <see cref="IAttachmentStore.OpenReadAsync"/>. Rationale:
/// <c>docs/14-COK-MODLULUK.md</c>, section 14.1 — the message body must stay small.
/// </remarks>
public sealed record AttachmentDescriptor
{
    /// <summary>Gets the attachment id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant the attachment belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the session the attachment was uploaded to. <see langword="null"/> for an upload without a session.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the run that produced the attachment. <see langword="null"/> for a user upload.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Gets the original file name.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the validated MIME type. It is the result of the magic-byte check, not the
    /// <c>Content-Type</c> the client sent.
    /// </summary>
    public required string MediaType { get; init; }

    /// <summary>Gets the size of the content in bytes.</summary>
    public required long ByteSize { get; init; }

    /// <summary>Gets the SHA-256 digest of the content (hexadecimal, upper case).</summary>
    public required string Sha256 { get; init; }

    /// <summary>Gets the actor that uploaded it, or <see langword="null"/> when it is unknown.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Gets the upload time.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
