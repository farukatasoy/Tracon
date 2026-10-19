using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.MafFeatures;

/// <summary>Registers the one tool all three agents of this sample share.</summary>
/// <remarks>
/// A tool is registered in code, with its policy, and every agent finds it by name.
/// A declarative agent lists the name in <see cref="AgentDefinition.ToolNames"/>; a
/// code-built agent asks <see cref="IToolRegistry"/> for the wrapped tool.
/// </remarks>
public static class SearchOrdersToolRegistration
{
    /// <summary>The name the tool registers under.</summary>
    public const string ToolName = "search_orders";

    /// <summary>Adds the tool to the host's services.</summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSearchOrdersTool(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(static _ => new TraconToolRegistration(
            AIFunctionFactory.Create((Func<string, string>)SearchOrders, ToolName, "Finds the open orders of a customer."))
        {
            Effect = ToolEffect.Read,
        });

        return services;
    }

    private static string SearchOrders(string customer)
        => $"Open orders for {customer}: 1042, 1043.";
}
