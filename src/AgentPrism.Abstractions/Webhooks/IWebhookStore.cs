namespace AgentPrism;

/// <summary>The store for webhook subscriptions and delivery history.</summary>
public interface IWebhookStore
{
    /// <summary>Lists a tenant's subscriptions.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The subscriptions.</returns>
    ValueTask<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single subscription by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The subscription name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The subscription; <see langword="null"/> if it does not exist.</returns>
    ValueTask<WebhookSubscription?> GetSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all enabled subscriptions subscribed to a specific event.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="eventType">The event name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching subscriptions. An empty list if none.</returns>
    ValueTask<IReadOnlyList<WebhookSubscription>> FindForEventAsync(
        string tenantId,
        string eventType,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a subscription.</summary>
    /// <param name="subscription">The subscription.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved subscription.</returns>
    ValueTask<WebhookSubscription> SaveSubscriptionAsync(
        WebhookSubscription subscription,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a subscription and its delivery history.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The subscription name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeleteSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a subscription's consecutive-failure counter, and disables the
    /// subscription if the threshold is exceeded.
    /// </summary>
    /// <param name="subscriptionId">The subscription identifier.</param>
    /// <param name="succeeded">Whether the most recent delivery succeeded.</param>
    /// <param name="disableThreshold">
    /// The subscription is disabled after this many consecutive failures.
    /// </param>
    /// <param name="updatedAt">The update time (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the subscription was disabled during this call.</returns>
    ValueTask<bool> RecordSubscriptionOutcomeAsync(
        Guid subscriptionId,
        bool succeeded,
        int disableThreshold,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a delivery record.</summary>
    /// <param name="delivery">The delivery.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created record.</returns>
    ValueTask<WebhookDelivery> CreateDeliveryAsync(
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a single delivery record.</summary>
    /// <param name="deliveryId">The delivery identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record; <see langword="null"/> if it does not exist.</returns>
    ValueTask<WebhookDelivery?> GetDeliveryAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a delivery attempt's result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask RecordDeliveryResultAsync(
        WebhookDeliveryResult result,
        CancellationToken cancellationToken = default);

    /// <summary>Lists delivery history. The newest record is returned first.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(
        WebhookDeliveryQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The component that publishes an event to its subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// Core code calls this; delivery <strong>does not happen in the request</strong>,
/// it is written to a queue. This way a slow or unreachable recipient does
/// not slow down the main path.
/// </para>
/// <para>
/// The observability rule applies here too: if publishing fails, the run
/// <strong>continues</strong>, the error is only logged.
/// </para>
/// </remarks>
public interface IWebhookPublisher
{
    /// <summary>Writes an event to the queue.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="eventType">The event name. See <see cref="WebhookEvents"/>.</param>
    /// <param name="payload">The event summary. Serialized and written to the body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deliveries created. 0 if there is no subscriber.</returns>
    ValueTask<int> PublishAsync(
        string tenantId,
        string eventType,
        WebhookEventPayload payload,
        CancellationToken cancellationToken = default);
}
