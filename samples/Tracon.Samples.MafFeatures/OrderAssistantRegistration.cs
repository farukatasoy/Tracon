using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>Registers a factory agent: a code-built agent that keeps the model rings.</summary>
public static class OrderAssistantRegistration
{
    /// <summary>The name the agent registers under.</summary>
    public const string AgentName = "order-assistant";

    /// <summary>Adds the factory agent to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="model">The model the agent talks to.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// The factory runs on every resolve and returns any Microsoft Agent Framework
    /// agent. Tracon records the run and applies its decorators, but the agent skips the
    /// definition compiler, so the factory has to ask for the pieces it wants to keep.
    /// </remarks>
    public static ITraconBuilder AddOrderAssistant(this ITraconBuilder builder, ModelBinding model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);

        return builder.AddAgent(
            AgentName,
            services =>
            {
                // The registry builds the model rings: run budget, circuit breaker,
                // fallback, response cache, content guards, and the 'chat' span. This
                // synchronous call applies no tenant key and no egress policy.
                var chatClient = services.GetRequiredService<IModelProviderRegistry>().CreateChatClient(model);

                // The registry returns the wrapped tool: authorization, validation,
                // timeout, truncation, and approval stay on.
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

                    // Tracon's history store, so the console and /v1/conversations show the
                    // turns. With SQL storage, leaving this out leaves both of them empty.
                    ChatHistoryProvider = services.GetService<ChatHistoryProvider>(),

                    // Any framework provider or option is now yours.
                    AIContextProviders = [new TodoProvider(new TodoProviderOptions())],
                });
            },
            description: "A factory agent that answers questions about orders.");
    }
}
