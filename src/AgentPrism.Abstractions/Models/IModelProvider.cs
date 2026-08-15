using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// A model provider. Each provider is implemented in its own package (for
/// example, <c>AgentPrism.OpenAI</c>) and registered with DI.
/// </summary>
/// <remarks>
/// This abstraction ensures that adding a new provider is <em>not a breaking
/// change</em>. This is the main rationale for the modular packaging decision.
/// </remarks>
public interface IModelProvider
{
    /// <summary>
    /// The provider name. <see cref="ModelBinding.Provider"/> matches this
    /// value. Comparison is case-insensitive.
    /// </summary>
    string Name { get; }

    /// <summary>The models this provider offers.</summary>
    IReadOnlyList<ModelDescriptor> Models { get; }

    /// <summary>
    /// Produces a <strong>raw</strong> chat client for the given binding.
    /// </summary>
    /// <param name="binding">The model binding.</param>
    /// <returns>
    /// The provider-specific client. Decorators <em>specific</em> to the
    /// provider (example: Anthropic's settings decorator) may be added here.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🚨 <strong>Do not build the common pipeline here.</strong>
    /// <c>UseFunctionInvocation()</c>, <c>UseOpenTelemetry()</c>, the content
    /// guard, the circuit breaker, and extra resolution are added by
    /// <c>ModelProviderRegistry.CreateChatClient</c>. Until Phase 48, every
    /// provider package built the tool-call loop inside itself; the result
    /// was that no ring the registry wraps around could see the loop's turns
    /// — a tool result entered the model uninspected.
    /// </para>
    /// <para>
    /// If the loop is also built here, two nested <c>FunctionInvokingChatClient</c>
    /// instances form: the inner one resolves tools, the outer one never sees
    /// any call. The damage is not functional but measurable (double
    /// wrapping, a misleading span tree).
    /// </para>
    /// </remarks>
    IChatClient CreateChatClient(ModelBinding binding);
}
