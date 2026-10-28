using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.CustomTool;

/// <summary>Registers the scoped fulfillment tool with its complete metadata.</summary>
public static class OrderFulfillmentToolRegistration
{
    /// <summary>Adds the tool and its required scoped dependency boundary.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The same chain.</returns>
    /// <remarks>
    /// The tool factory runs after the final service provider exists.
    /// It can therefore pass the provider-owned <see cref="IServiceScopeFactory"/>
    /// to the tool without building a second container during startup.
    /// </remarks>
    public static ITraconBuilder AddOrderFulfillmentTool(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<OrderFulfillmentTool>();

        return builder.AddTool(
            static provider => provider.GetRequiredService<OrderFulfillmentTool>().CreateFunction(),
            static options =>
            {
                options.RequiresApproval = true;
                options.Effect = ToolEffect.External;
                options.RequiredPermission = "orders.submit";
                options.Timeout = TimeSpan.FromSeconds(30);
                options.SafeToRepeat = true;
                options.MaxOutputBytes = 768;
            });
    }
}
