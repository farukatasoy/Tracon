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
    [AgentPrismTool(
        "cancel_order",
        "Cancels an order.",
        RequiresApproval = true,
        Effect = ToolEffect.Destructive,
        RequiredPermission = "orders.cancel")]
    public static string CancelOrder(string orderId)
        => $"Order {orderId} has been canceled.";

    /// <summary>
    /// Demo tool for F-114 (docs/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md): its
    /// body sleeps far longer than its own 1-second timeout, showing that the
    /// registry's <c>TimeoutAIFunction</c> wrapper cuts the WAIT short — the
    /// body itself keeps running in the background, a documented limit of
    /// cooperative cancellation (Manual Case 8).
    /// </summary>
    /// <param name="reportId">The report number.</param>
    /// <returns>The report text — never actually reached at the default timeout.</returns>
    [AgentPrismTool("get_slow_report", "Fetches a report that is slow to generate.", TimeoutSeconds = 1)]
    public static async Task<string> GetSlowReport(string reportId)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        return $"Report {reportId} is ready.";
    }
}
