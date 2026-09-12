namespace Tracon;

/// <summary>
/// The store for idempotency records.
/// </summary>
/// <remarks>
/// <para>
/// A request that carries an <c>Idempotency-Key</c> header is processed <strong>at most
/// once</strong>, exactly as the HTTP <c>Idempotency-Key</c> standard (the pattern
/// Stripe follows) prescribes: while the first request is running, a second request
/// with the same key either gets the stored response, or (if still processing) gets
/// <c>409</c>.
/// </para>
/// <para>
/// The default (in-memory) setup registers the first-class
/// <c>InMemoryIdempotencyStore</c>; it is sufficient for a single-instance deployment.
/// A multi-instance deployment requires a SQL provider — otherwise each instance keeps
/// its own key set and deduplication is lost across instances.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Tries to reserve the key as <c>Reserved</c>. If the key already exists,
    /// the existing record's state is returned and no new record is OPENED.
    /// </summary>
    /// <param name="request">The reservation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reservation result.</returns>
    /// <remarks>
    /// The reservation must be <strong>atomic</strong>: if two concurrent
    /// requests arrive with the same key, only one must get
    /// <see cref="IdempotencyState.Reserved"/>, the other
    /// <see cref="IdempotencyState.InProgress"/>.
    /// </remarks>
    ValueTask<IdempotencyReservation> ReserveAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Records the completed response and sets the key's state to <c>Completed</c>.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="key">The key.</param>
    /// <param name="response">The response to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask CompleteAsync(
        string tenantId,
        string key,
        IdempotencyResponse response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the record after a failed request; a retry with the same key
    /// becomes free again.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// A failed run's record is NOT kept. The purpose of idempotency is to
    /// make retries safe; keeping a failure would mean the client can never
    /// retry after a transient error.
    /// </remarks>
    ValueTask ReleaseAsync(
        string tenantId,
        string key,
        CancellationToken cancellationToken = default);
}
