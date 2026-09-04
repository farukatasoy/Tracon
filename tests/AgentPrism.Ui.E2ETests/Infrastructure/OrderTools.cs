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

    /// <summary>
    /// Marks an order's preview as ready and writes a consumer-defined
    /// <see cref="RunEventType.Custom"/> event (phase 141's escape hatch, for the E2E test).
    /// </summary>
    /// <param name="orderId">Order number.</param>
    /// <returns>A human-readable result message.</returns>
    [AgentPrismTool("mark_preview_ready", "Marks an order's preview as ready.")]
    [Description("Marks an order's preview as ready.")]
    public static async Task<string> MarkPreviewReady([Description("Order number")] string orderId)
    {
        var writer = AgentPrismRunContext.Current?.Writer;

        if (writer is not null)
        {
            await writer.AppendAsync(new RunEventDraft(RunEventType.Custom)
            {
                CustomType = "contoso.preview-ready",
                Payload = $$"""{"orderId":"{{orderId}}"}""",
            });
        }

        return $"Preview for order {orderId} is ready.";
    }
}
