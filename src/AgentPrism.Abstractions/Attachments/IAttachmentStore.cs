namespace AgentPrism;

/// <summary>The contract for attachment metadata and for the default (database) content storage.</summary>
/// <remarks>
/// When <see cref="IAttachmentStorage"/> is registered the content lives there and this
/// store keeps the metadata only; when it is not registered the content is stored
/// directly in the database (<c>bytea</c>). Rationale:
/// <c>docs/14-COK-MODLULUK.md</c>, section 14.3.
/// </remarks>
public interface IAttachmentStore
{
    /// <summary>Saves a new attachment.</summary>
    /// <param name="content">The content and metadata to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The attachment record with its id and digest assigned.</returns>
    ValueTask<AttachmentDescriptor> SaveAsync(AttachmentContent content, CancellationToken cancellationToken = default);

    /// <summary>Reads the metadata of a single attachment.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="id">The attachment id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when it does not exist or belongs to another tenant.</returns>
    ValueTask<AttachmentDescriptor?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Opens the raw content of an attachment as a stream.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="id">The attachment id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A readable stream, or <see langword="null"/> when the record does not exist or belongs to another tenant.</returns>
    ValueTask<Stream?> OpenReadAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists the attachments through a filter.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records ordered from newest to oldest.</returns>
    ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(AttachmentQuery query, CancellationToken cancellationToken = default);

    /// <summary>Deletes an attachment.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="id">The attachment id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when a record was deleted.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every attachment of a session.
    /// </summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records deleted.</returns>
    /// <remarks>
    /// It is called when a session is deleted; it keeps orphaned attachments from piling
    /// up. Rationale: <c>docs/14-COK-MODLULUK.md</c>, open question 2.
    /// </remarks>
    ValueTask<int> DeleteBySessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The extension point that stores attachment content in an external store (S3, Blob).
/// </summary>
/// <remarks>
/// When it is not registered the content lives in the database (K1 — no surprises).
/// AgentPrism takes no dependency on any cloud SDK; the consumer writes the
/// implementation. Rationale: <c>docs/KARARLAR.md</c>, K-007.
/// </remarks>
public interface IAttachmentStorage
{
    /// <summary>Writes the content to the external store.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="id">The attachment id.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="mediaType">The MIME type of the content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The location of the content in the external store.</returns>
    ValueTask<Uri> WriteAsync(
        string tenantId,
        Guid id,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the content from the external store.</summary>
    /// <param name="uri">The location returned by <see cref="WriteAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A readable stream, or <see langword="null"/> when the location no longer exists.</returns>
    ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>Deletes the content in the external store.</summary>
    /// <param name="uri">The location returned by <see cref="WriteAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default);
}
