using Microsoft.Agents.AI;

namespace Tracon.Samples.MafFeatures;

/// <summary>
/// Registers an agent extension: a full context provider on an agent that is defined as data.
/// </summary>
public static class PolicyNoticeExtensionRegistration
{
    /// <summary>The name a definition writes in <c>ExtensionNames</c>.</summary>
    public const string ExtensionName = "policy-notice";

    /// <summary>The instruction the extension's provider adds to every model call.</summary>
    public const string Notice = "Refunds above 100 need a manager's approval.";

    /// <summary>Adds the extension to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// The extension runs when an agent that names it is compiled. The agent still goes through
    /// the definition compiler, so it keeps the model rings, the tenant's own key, the egress
    /// policy, and cost. The compiler refuses a change to a member the definition owns.
    /// </remarks>
    public static ITraconBuilder AddPolicyNotice(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddAgentExtension(
            ExtensionName,
            context =>
            {
                // Add after the providers Tracon built; the compiler refuses a change to them.
                var options = context.ChatAgentOptions!;
                options.AIContextProviders = [.. options.AIContextProviders ?? [], new PolicyNoticeProvider()];
            },
            description: "Adds the refund policy to the instructions of every model call.");
    }

    private sealed class PolicyNoticeProvider : AIContextProvider
    {
        protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
            => new(new AIContext { Instructions = Notice });
    }
}
