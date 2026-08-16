using System.ComponentModel;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>The only tool used in the tests.</summary>
/// <remarks>
/// Tools are declared only in code. This class provides a real registration so
/// the UI can validate its tool list and tool card.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Returns the status of an order.</summary>
    /// <param name="orderId">Order number.</param>
    /// <returns>A human-readable status message.</returns>
    [AgentPrismTool("get_order_status", "Returns the shipping status of an order.")]
    [Description("Returns the shipping status of an order.")]
    public static string GetOrderStatus([Description("Order number")] string orderId)
        => $"Order {orderId} has shipped.";

    /// <summary>Cancels an order. Requires approval (for the phase 55 E2E test).</summary>
    /// <param name="orderId">Order number.</param>
    /// <returns>A human-readable result message.</returns>
    [AgentPrismTool("cancel_order", "Cancels an order.", RequiresApproval = true)]
    [Description("Cancels an order.")]
    public static string CancelOrder([Description("Order number")] string orderId)
        => $"Order {orderId} has been canceled.";
}
