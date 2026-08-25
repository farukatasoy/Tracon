using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Samples.CustomTool;

/// <summary>Registers the scoped fulfillment tool with its complete metadata.</summary>
public static class OrderFulfillmentToolRegistration
{
    /// <summary>Adds the tool and its required scoped dependency boundary.</summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// The registration factory runs after the final service provider exists.
    /// It can therefore pass the provider-owned <see cref="IServiceScopeFactory"/>
    /// to the tool without building a second container during startup.
    /// </remarks>
    public static IServiceCollection AddOrderFulfillmentTool(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<OrderFulfillmentTool>();
        services.AddSingleton(static provider => new AgentPrismToolRegistration(
            provider.GetRequiredService<OrderFulfillmentTool>().CreateFunction(),
            requiresApproval: true,
            effect: ToolEffect.External,
            requiredPermission: "orders.submit",
            timeout: TimeSpan.FromSeconds(30),
            safeToRepeat: true,
            maxOutputBytes: 768));

        return services;
    }
}
