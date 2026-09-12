using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>The registry that keeps registered model providers by name.</summary>
public interface IModelProviderRegistry
{
    /// <summary>Returns the definitions of the registered providers.</summary>
    /// <returns>The provider definitions.</returns>
    IReadOnlyList<ModelProviderDescriptor> List();

    /// <summary>
    /// Produces a chat client for the given binding, using the provider's
    /// setup-time credential.
    /// </summary>
    /// <param name="binding">The model binding.</param>
    /// <returns>The chat client.</returns>
    /// <exception cref="TraconException">
    /// No provider is registered with the name in <see cref="ModelBinding.Provider"/>.
    /// </exception>
    /// <remarks>
    /// This overload does <strong>not</strong> resolve a tenant provider
    /// binding (BYOK) or check an egress policy: both need
    /// an async store lookup, which this synchronous method cannot perform.
    /// Callers that must honor a tenant's own credential and egress policy —
    /// this includes the real agent-run compile path — use
    /// <see cref="CreateChatClientAsync(ModelBinding, CancellationToken)"/> instead.
    /// </remarks>
    IChatClient CreateChatClient(ModelBinding binding);

    /// <summary>
    /// Produces a chat client for the given binding, first resolving the
    /// current tenant's own provider credential and egress policy.
    /// </summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chat client.</returns>
    /// <exception cref="TraconException">
    /// No provider is registered with the name in <see cref="ModelBinding.Provider"/>,
    /// the tenant's egress policy does not allow the provider, or the
    /// tenant's provider binding cannot be resolved to a credential value.
    /// </exception>
    /// <remarks>
    /// When no tenant context is registered, or the tenant has no binding for
    /// the provider, behavior is identical to <see cref="CreateChatClient"/>
    /// (the no-surprises rule: behavior does not change until BYOK is configured).
    /// </remarks>
    ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a chat client after applying the current tenant's egress policy.
    /// </summary>
    /// <param name="binding">The model binding.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chat client.</returns>
    ValueTask<IChatClient> CreateSetupChatClientAsync(
        ModelBinding binding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether the current tenant has its own provider binding for
    /// <paramref name="binding"/>'s primary provider or any of its
    /// <see cref="ModelBinding.Fallbacks"/> (BYOK).
    /// </summary>
    /// <param name="binding">The model binding to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if a tenant-specific credential would be baked
    /// into the chat client <see cref="CreateChatClientAsync(ModelBinding, CancellationToken)"/> produces for
    /// this binding.
    /// </returns>
    /// <remarks>
    /// A compiled agent's chat client is a fixed pipeline object — once a
    /// tenant's credential is resolved into it, changing or deleting the
    /// underlying binding has no further effect on that object. A cache
    /// keyed only by definition identity (<c>CompiledAgentCache</c>) must
    /// therefore never hold an agent built with a tenant-specific credential;
    /// callers use this method to decide whether to bypass that cache.
    /// </remarks>
    ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default);
}
