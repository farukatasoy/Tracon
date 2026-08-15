using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>The registry that keeps registered model providers by name.</summary>
public interface IModelProviderRegistry
{
    /// <summary>Returns the definitions of the registered providers.</summary>
    /// <returns>The provider definitions.</returns>
    IReadOnlyList<ModelProviderDescriptor> List();

    /// <summary>Produces a chat client for the given binding.</summary>
    /// <param name="binding">The model binding.</param>
    /// <returns>The chat client.</returns>
    /// <exception cref="AgentPrismException">
    /// No provider is registered with the name in <see cref="ModelBinding.Provider"/>.
    /// </exception>
    IChatClient CreateChatClient(ModelBinding binding);
}
