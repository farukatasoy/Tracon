namespace Tracon.Samples.MafFeatures;

/// <summary>Puts the doors into one host.</summary>
public static class MafFeaturesRegistration
{
    /// <summary>The declarative agent the decorator wraps.</summary>
    public const string SupportAgentName = "support-agent";

    /// <summary>Registers the tool, the decorator, the two extensions, and the four agents.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="model">The model every agent talks to.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>support-agent</c> is defined as data. The decorator adds framework middleware to it, the <c>policy-notice</c> extension adds a context provider, and the <c>model-call-log</c> extension adds a chat client ring.</description></item>
    /// <item><description><c>order-assistant</c> is a factory agent that declares its model: code builds the agent, Tracon builds the client, and the tenant key, egress policy, and cost apply.</description></item>
    /// <item><description><c>plain-order-assistant</c> is a factory agent that declares no model: code builds it, the registry supplies the model rings, and the tenant key, egress policy, and cost do not apply.</description></item>
    /// <item><description><c>invoice-assistant</c> comes from a custom source: code builds it and the tenant key, egress policy, and cost apply.</description></item>
    /// </list>
    /// </remarks>
    public static ITraconBuilder AddMafFeatures(this ITraconBuilder builder, ModelBinding model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);

        return builder
            .AddSearchOrdersTool()
            .AddToolCallLog()
            .AddPolicyNotice()
            .AddModelCallLog()
            .AddAgent(new AgentDefinition
            {
                Name = SupportAgentName,
                Instructions = "Answer questions about orders. Look orders up with the search tool.",
                Model = model,
                ToolNames = [SearchOrdersToolRegistration.ToolName],
                ExtensionNames = [PolicyNoticeExtensionRegistration.ExtensionName, ModelCallLogExtensionRegistration.ExtensionName],
            })
            .AddOrderAssistant(model)
            .AddPlainOrderAssistant(model)
            .AddInvoiceAssistant(model);
    }
}
