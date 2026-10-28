using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>Registers a factory agent that declares its model: code builds the agent, Tracon builds the client.</summary>
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
    /// agent. Because the agent declares its model, Tracon builds the chat client with the
    /// tenant's own key and the tenant's egress policy and hands it to the factory as
    /// <see cref="AgentFactoryContext.ChatClient"/>. The run record names the model and prices
    /// the run. The agent skips the definition compiler, so the factory still asks for the
    /// pieces it wants to keep, such as the wrapped tool.
    /// </remarks>
    public static ITraconBuilder AddOrderAssistant(this ITraconBuilder builder, ModelBinding model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);

        return builder.AddAgent(
            AgentName,
            model,
            context =>
            {
                // The registry returns the wrapped tool: authorization, validation,
                // timeout, truncation, and approval stay on.
                if (!context.Services.GetRequiredService<IToolRegistry>().TryGet(SearchOrdersToolRegistration.ToolName, out var searchOrders))
                {
                    throw new InvalidOperationException($"The tool '{SearchOrdersToolRegistration.ToolName}' is not registered.");
                }

                // The client carries the model rings (run budget, circuit breaker, fallback,
                // response cache, content guards, the 'chat' span), the tenant's key, and the
                // egress policy. Build the agent around it and nothing else.
                return context.ChatClient.AsAIAgent(new ChatClientAgentOptions
                {
                    Name = context.AgentName,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = "Answer questions about orders. Look orders up with the search tool.",
                        Tools = [searchOrders],
                    },

                    // Tracon's history store, so the console and /v1/conversations show the
                    // turns. With SQL storage, leaving this out leaves both of them empty.
                    ChatHistoryProvider = context.Services.GetService<ChatHistoryProvider>(),

                    // Any framework provider or option is now yours.
                    AIContextProviders = [new TodoProvider(new TodoProviderOptions())],
                });
            },
            description: "A factory agent that declares its model and answers questions about orders.");
    }
}
