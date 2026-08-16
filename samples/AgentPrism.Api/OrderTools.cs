namespace AgentPrism.Api;

/// <summary>
/// Sample tools. A real application would call a database or a service here.
/// </summary>
/// <remarks>
/// Methods are marked with <c>[AgentPrismTool]</c> and registered with
/// <c>builder.AddAgentPrism().AddToolsFrom(typeof(OrderTools))</c>. An unmarked
/// method is not a tool — adding a new helper method to this class does not
/// automatically expose it to agents. The UI only lets the user pick from this
/// list; it never generates tool code.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Returns the shipping status of an order.</summary>
    /// <param name="orderId">The order number.</param>
    /// <returns>The shipping status text.</returns>
    [AgentPrismTool("get_order_status", "Returns the shipping status of an order.")]
    public static string GetOrderStatus(string orderId)
        => $"Order {orderId} has shipped. Estimated delivery: 2 days.";

    /// <summary>Lists a customer's recent orders.</summary>
    /// <param name="customerId">The customer number.</param>
    /// <returns>The order list text.</returns>
    [AgentPrismTool("list_recent_orders", "Lists a customer's recent orders.")]
    public static string ListRecentOrders(string customerId)
        => $"Recent orders for customer {customerId}: ORD-1001, ORD-1002.";

    /// <summary>Cancels an order.</summary>
    /// <param name="orderId">The order number.</param>
    /// <returns>The cancellation result text.</returns>
    /// <remarks>
    /// <c>RequiresApproval = true</c>: this tool performs an irreversible action
    /// and must not run automatically on the model's own decision. A marked tool
    /// is wrapped with <c>ApprovalRequiredAIFunction</c> in the registry; the
    /// Microsoft Agent Framework produces an approval request instead of running
    /// the call, and the UI shows an approval card.
    /// </remarks>
    [AgentPrismTool("cancel_order", "Cancels an order.", RequiresApproval = true)]
    public static string CancelOrder(string orderId)
        => $"Order {orderId} has been canceled.";
}
