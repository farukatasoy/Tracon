using System.ComponentModel;
using Tracon;

namespace Tracon.Starter;

/// <summary>
/// Sample tool. In a real application, this would call a database or service.
/// </summary>
/// <remarks>
/// The method is marked with <c>[TraconTool]</c> and is registered at build
/// time by the source generator through
/// <c>builder.AddTracon().AddGeneratedTools()</c> — no reflection, no AOT
/// warning. An unmarked method is not a tool — adding a new helper method to
/// this class does not automatically expose it to agents. Tools are defined
/// only in code; the UI only lets users pick from this list.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Returns the shipping status of an order.</summary>
    /// <param name="orderId">The order number.</param>
    /// <returns>The shipping status text.</returns>
    [TraconTool("get_order_status", "Returns the shipping status of an order.")]
    public static string GetOrderStatus([Description("The order number.")] string orderId)
        => $"Order {orderId} has shipped. Estimated delivery: 2 days.";
}
