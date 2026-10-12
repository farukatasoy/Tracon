namespace Tracon;

/// <summary>The contract for attachment metadata and for the default (database) content storage.</summary>
/// <remarks>
/// <para>
/// When <see cref="IAttachmentStorage"/> is registered the content lives there and this
/// store keeps the metadata only; when it is not registered the content is stored
/// directly in the database (<c>bytea</c>).
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
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
    /// <returns>
    /// A readable stream, or <see langword="null"/> when the record does not exist or
    /// belongs to another tenant.
    /// </returns>
    /// <remarks>
    /// <strong>Stream ownership.</strong> The CALLER owns the returned stream and must
    /// dispose it; this store does not dispose it and does not keep a reference to it
    /// after returning.
    /// </remarks>
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
    /// <remarks>
    /// When the content lives in <see cref="IAttachmentStorage"/>, the content is
    /// deleted FIRST and the record second. If the content delete throws, the
    /// record stays and the exception propagates: the record is the only place
    /// that says where the content is, so a retry can still find it.
    /// </remarks>
    ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every attachment of a session.
    /// </summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of records deleted.</returns>
    /// <remarks>
    /// <para>
    /// It is called when a session is deleted; it keeps orphaned attachments from piling
    /// up.
    /// </para>
    /// <para>
    /// Each attachment follows the order of <see cref="DeleteAsync"/>: content first,
    /// record second. The first content delete that throws stops the call; that
    /// record and every record not yet reached stay, so a retry deletes the rest.
    /// </para>
    /// </remarks>
    ValueTask<int> DeleteBySessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The extension point that stores attachment content in an external store (S3, Blob).
/// </summary>
/// <remarks>
/// <para>
/// When it is not registered the content lives in the database, so nothing has to be set up first.
/// Tracon takes no dependency on any cloud SDK; the consumer writes the
/// implementation.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton, optional.</strong> No default implementation is
/// registered. A consumer that registers one must register it as a singleton (or
/// hand it to <c>IServiceProvider.GetService&lt;IAttachmentStorage&gt;()</c>'s
/// caller, itself resolved once as a singleton dependency, for example
/// <c>InMemoryAttachmentStore</c>) — never scoped, which would be a captive
/// dependency.
/// </para>
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
    /// <remarks>
    /// <strong>Stream ownership.</strong> The CALLER owns <paramref name="content"/>
    /// and disposes it; this method reads from it but does not dispose it.
    /// </remarks>
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
    /// <remarks>
    /// <strong>Stream ownership.</strong> The CALLER owns the returned stream and
    /// must dispose it.
    /// </remarks>
    ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default);

    /// <summary>Deletes the content in the external store.</summary>
    /// <param name="uri">The location returned by <see cref="WriteAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the content is gone.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Must be idempotent.</strong> Deleting a location that no longer
    /// exists succeeds and does not throw. Tracon deletes the content BEFORE the
    /// metadata row, so a retry after a partial failure — or two deletes that
    /// race — calls this method again for content that is already gone.
    /// </para>
    /// <para>
    /// Throw only when the content may still exist. The attachment store, the
    /// retention policy, and data subject erasure then keep the metadata row,
    /// because it is the only record of where the content is: a single delete
    /// and an erasure fail, and retention retries the row on its next run.
    /// </para>
    /// </remarks>
    ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default);
}
