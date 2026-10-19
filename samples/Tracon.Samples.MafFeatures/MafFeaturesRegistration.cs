using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>Puts the three doors into one host.</summary>
public static class MafFeaturesRegistration
{
    /// <summary>The declarative agent the decorator wraps.</summary>
    public const string SupportAgentName = "support-agent";

    /// <summary>Registers the tool, the decorator, and the three agents.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="model">The model every agent talks to.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>support-agent</c> is defined as data. The decorator adds framework middleware to it.</description></item>
    /// <item><description><c>order-assistant</c> is a factory agent: code builds it, the registry supplies the model rings.</description></item>
    /// <item><description><c>invoice-assistant</c> comes from a custom source: code builds it and the tenant key, egress policy, and cost still apply.</description></item>
    /// </list>
    /// </remarks>
    public static ITraconBuilder AddMafFeatures(this ITraconBuilder builder, ModelBinding model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);

        builder.Services.AddSearchOrdersTool();

        return builder
            .AddToolCallLog()
            .AddAgent(new AgentDefinition
            {
                Name = SupportAgentName,
                Instructions = "Answer questions about orders. Look orders up with the search tool.",
                Model = model,
                ToolNames = [SearchOrdersToolRegistration.ToolName],
            })
            .AddOrderAssistant(model)
            .AddInvoiceAssistant(model);
    }
}
