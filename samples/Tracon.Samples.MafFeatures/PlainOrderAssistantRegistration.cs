using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>Registers a factory agent that declares no model, the form for an agent that does not call a model through Tracon.</summary>
public static class PlainOrderAssistantRegistration
{
    /// <summary>The name the agent registers under.</summary>
    public const string AgentName = "plain-order-assistant";

    /// <summary>Adds the factory agent to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="model">The model the agent talks to.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// Tracon records the run and applies its decorators, but it does not know which model the
    /// agent calls. The client the factory builds from the registry is built without the
    /// tenant, so the agent uses the key set up at start, skips the tenant's egress policy,
    /// and its run record has no model and no cost. Tracon writes one warning for the agent
    /// and names the overload that fixes it.
    /// </remarks>
    public static ITraconBuilder AddPlainOrderAssistant(this ITraconBuilder builder, ModelBinding model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);

        return builder.AddAgent(
            AgentName,
            services =>
            {
                // This synchronous call applies no tenant key and no egress policy.
                var chatClient = services.GetRequiredService<IModelProviderRegistry>().CreateChatClient(model);

                if (!services.GetRequiredService<IToolRegistry>().TryGet(SearchOrdersToolRegistration.ToolName, out var searchOrders))
                {
                    throw new InvalidOperationException($"The tool '{SearchOrdersToolRegistration.ToolName}' is not registered.");
                }

                return chatClient.AsAIAgent(new ChatClientAgentOptions
                {
                    Name = AgentName,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = "Answer questions about orders. Look orders up with the search tool.",
                        Tools = [searchOrders],
                    },
                });
            },
            description: "A factory agent that declares no model.");
    }
}
