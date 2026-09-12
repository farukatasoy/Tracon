using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// The default implementation that keeps webhook subscriptions and delivery history
/// in process memory.
/// </summary>
/// <remarks>
/// For single-process deployments and tests. <c>UsePostgreSql()</c> replaces it
/// with <c>PostgresWebhookStore</c>.
/// </remarks>
internal sealed class InMemoryWebhookStore : IWebhookStore
{
    private readonly ConcurrentDictionary<Guid, WebhookSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<Guid, WebhookDelivery> _deliveries = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        IReadOnlyList<WebhookSubscription> result = _subscriptions.Values
            .Where(subscription => string.Equals(subscription.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(subscription => subscription.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<WebhookSubscription>>(result);
    }

    /// <inheritdoc />
    public ValueTask<WebhookSubscription?> GetSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var found = _subscriptions.Values.FirstOrDefault(subscription =>
            string.Equals(subscription.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(subscription.Name, name, StringComparison.Ordinal));

        return new ValueTask<WebhookSubscription?>(found);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WebhookSubscription>> FindForEventAsync(
        string tenantId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        IReadOnlyList<WebhookSubscription> result = _subscriptions.Values
            .Where(subscription =>
                subscription.Enabled
                && string.Equals(subscription.TenantId, tenantId, StringComparison.Ordinal)
                && subscription.Events.Contains(eventType, StringComparer.Ordinal))
            .OrderBy(subscription => subscription.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<WebhookSubscription>>(result);
    }

    /// <inheritdoc />
    public ValueTask<WebhookSubscription> SaveSubscriptionAsync(
        WebhookSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var existing = _subscriptions.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.TenantId, subscription.TenantId, StringComparison.Ordinal)
            && string.Equals(candidate.Name, subscription.Name, StringComparison.Ordinal));

        if (existing is not null && existing.Id != subscription.Id)
        {
            _subscriptions.TryRemove(existing.Id, out _);
        }

        var saved = subscription with { Id = existing?.Id ?? subscription.Id };
        _subscriptions[saved.Id] = saved;

        return new ValueTask<WebhookSubscription>(saved);
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = _subscriptions.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(candidate.Name, name, StringComparison.Ordinal));

        if (existing is null || !_subscriptions.TryRemove(existing.Id, out _))
        {
            return new ValueTask<bool>(false);
        }

        // Persistence uses ON DELETE CASCADE. The in-memory store does this manually.
        foreach (var delivery in _deliveries.Values.Where(item => item.SubscriptionId == existing.Id))
        {
            _deliveries.TryRemove(delivery.Id, out _);
        }

        return new ValueTask<bool>(true);
    }

    /// <inheritdoc />
    public ValueTask<bool> RecordSubscriptionOutcomeAsync(
        Guid subscriptionId,
        bool succeeded,
        int disableThreshold,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(subscriptionId, out var subscription))
        {
            return new ValueTask<bool>(false);
        }

        var failures = succeeded ? 0 : subscription.ConsecutiveFailures + 1;
        var shouldDisable = !succeeded && disableThreshold > 0 && failures >= disableThreshold;

        _subscriptions[subscriptionId] = subscription with
        {
            ConsecutiveFailures = failures,
            Enabled = subscription.Enabled && !shouldDisable,
            UpdatedAt = updatedAt,
        };

        return new ValueTask<bool>(shouldDisable && subscription.Enabled);
    }

    /// <inheritdoc />
    public ValueTask<WebhookDelivery> CreateDeliveryAsync(
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        _deliveries[delivery.Id] = delivery;

        return new ValueTask<WebhookDelivery>(delivery);
    }

    /// <inheritdoc />
    public ValueTask<WebhookDelivery?> GetDeliveryAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default)
        => new(_deliveries.TryGetValue(deliveryId, out var delivery) ? delivery : null);

    /// <inheritdoc />
    public ValueTask RecordDeliveryResultAsync(
        WebhookDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_deliveries.TryGetValue(result.DeliveryId, out var delivery))
        {
            _deliveries[result.DeliveryId] = delivery with
            {
                Status = result.Status,
                Attempt = result.Attempt,
                ResponseCode = result.ResponseCode,
                Error = result.Error,
                DeliveredAt = result.Status == WebhookDeliveryStatus.Delivered
                    ? result.RecordedAt
                    : delivery.DeliveredAt,
            };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(
        WebhookDeliveryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matches = _deliveries.Values
            .Where(delivery => string.Equals(delivery.TenantId, query.TenantId, StringComparison.Ordinal));

        if (query.SubscriptionId is { } subscriptionId)
        {
            matches = matches.Where(delivery => delivery.SubscriptionId == subscriptionId);
        }

        if (query.Status is { } status)
        {
            matches = matches.Where(delivery => delivery.Status == status);
        }

        IReadOnlyList<WebhookDelivery> result = matches
            .OrderByDescending(delivery => delivery.CreatedAt)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Max(1, query.Take))
            .ToList();

        return new ValueTask<IReadOnlyList<WebhookDelivery>>(result);
    }
}
