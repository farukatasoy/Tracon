using Microsoft.Extensions.AI;

namespace Tracon.Samples.MafFeatures;

/// <summary>Registers the one tool every agent of this sample shares.</summary>
/// <remarks>
/// A tool is registered in code, with its policy, and every agent finds it by name.
/// A declarative agent lists the name in <see cref="AgentDefinition.ToolNames"/>; a
/// code-built agent asks <see cref="IToolRegistry"/> for the wrapped tool.
/// </remarks>
public static class SearchOrdersToolRegistration
{
    /// <summary>The name the tool registers under.</summary>
    public const string ToolName = "search_orders";

    /// <summary>Adds the tool to the Tracon chain.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <returns>The same chain.</returns>
    public static ITraconBuilder AddSearchOrdersTool(this ITraconBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddTool(
            AIFunctionFactory.Create((Func<string, string>)SearchOrders, ToolName, "Finds the open orders of a customer."),
            static options => options.Effect = ToolEffect.Read);
    }

    private static string SearchOrders(string customer)
        => $"Open orders for {customer}: 1042, 1043.";
}
