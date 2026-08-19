namespace AgentPrism;

/// <summary>The store for inbound trigger definitions.</summary>
public interface IInboundTriggerStore
{
    /// <summary>Fetches the trigger with the given name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The trigger name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trigger; <see langword="null"/> if it does not exist.</returns>
    ValueTask<InboundTrigger?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Lists all of a tenant's triggers, by name.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The triggers.</returns>
    ValueTask<IReadOnlyList<InboundTrigger>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the trigger (name + tenant is unique).</summary>
    /// <param name="trigger">The trigger to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved trigger.</returns>
    ValueTask<InboundTrigger> UpsertAsync(InboundTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>Deletes the trigger.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="name">The trigger name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}
