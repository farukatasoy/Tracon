namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A webhook publisher that keeps every event it is given, and can be told to fail.
/// </summary>
/// <remarks>
/// Registered as <see cref="IWebhookPublisher"/> in place of the delivery pipeline, so a test sees exactly what the engine
/// hands over: the event name, the tenant and the payload.
/// </remarks>
internal sealed class RecordingWebhookPublisher : IWebhookPublisher
{
    private readonly List<(string TenantId, string EventType, WebhookEventPayload Payload)> _events = [];

    /// <summary>Gets or sets whether the next calls throw, as a delivery store that is down does.</summary>
    public bool Throws { get; set; }

    /// <summary>Gets what was published, oldest first.</summary>
    public IReadOnlyList<(string TenantId, string EventType, WebhookEventPayload Payload)> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Gets the run summaries published under one event name.</summary>
    /// <param name="eventType">The event name.</param>
    /// <returns>The summaries, oldest first.</returns>
    public IReadOnlyList<WebhookRunSummary> Runs(string eventType)
        => [.. Events
            .Where(entry => string.Equals(entry.EventType, eventType, StringComparison.Ordinal))
            .Select(static entry => entry.Payload.Run.ShouldNotBeNull())];

    /// <inheritdoc />
    public ValueTask<int> PublishAsync(
        string tenantId,
        string eventType,
        WebhookEventPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (Throws)
        {
            throw new IOException("The delivery store is not reachable.");
        }

        lock (_events)
        {
            _events.Add((tenantId, eventType, payload));
        }

        return new ValueTask<int>(1);
    }
}
