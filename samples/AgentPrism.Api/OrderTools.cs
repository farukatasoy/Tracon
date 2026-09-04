using System.ComponentModel;
using System.Text.Json.Serialization;

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
    public static string GetOrderStatus([Description("The order number.")] string orderId)
        => $"Order {orderId} has shipped. Estimated delivery: 2 days.";

    /// <summary>Lists a customer's recent orders.</summary>
    /// <param name="customerId">The customer number.</param>
    /// <returns>The order list text.</returns>
    [AgentPrismTool("list_recent_orders", "Lists a customer's recent orders.")]
    public static string ListRecentOrders([Description("The customer number.")] string customerId)
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
    public static string CancelOrder([Description("The order number.")] string orderId)
        => $"Order {orderId} has been canceled.";

    /// <summary>
    /// Demo tool for F-114 (docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md): its
    /// body sleeps far longer than its own 1-second timeout, showing that the
    /// registry's <c>TimeoutAIFunction</c> wrapper cuts the WAIT short — the
    /// body itself keeps running in the background, a documented limit of
    /// cooperative cancellation (Manual Case 8).
    /// </summary>
    /// <param name="reportId">The report number.</param>
    /// <returns>The report text — never actually reached at the default timeout.</returns>
    [AgentPrismTool("get_slow_report", "Fetches a report that is slow to generate.", TimeoutSeconds = 1)]
    public static async Task<string> GetSlowReport([Description("The report number.")] string reportId)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        return $"Report {reportId} is ready.";
    }

    /// <summary>Estimates the shipping cost for a delivery address.</summary>
    /// <param name="address">The delivery address.</param>
    /// <returns>The shipping estimate text.</returns>
    /// <remarks>
    /// Demo tool for F-176 (docs/arsiv/fazlar/135-URETILEN-SEMANIN-NESNE-GRAFI.md): its one
    /// parameter is a supported OBJECT type (135.1), not a scalar — the
    /// generator produces a nested JSON Schema node for <see cref="ShippingAddress"/>
    /// and binds it through <see cref="ShippingAddressJsonContext"/>
    /// (135.5), never through reflection.
    /// </remarks>
    [AgentPrismTool(
        "estimate_shipping_cost",
        "Estimates the shipping cost for a delivery address.",
        JsonSerializerContext = typeof(ShippingAddressJsonContext))]
    public static string EstimateShippingCost([Description("The delivery address.")] ShippingAddress address)
        => $"Estimated shipping to {address.City}, {address.PostalCode}: $12.50 (3-5 business days).";

    /// <summary>Marks an order's preview as ready, for a customer to review before it ships.</summary>
    /// <param name="orderId">The order number.</param>
    /// <returns>A human-readable confirmation message.</returns>
    /// <remarks>
    /// Demo tool for F-187 (docs/arsiv/fazlar/141-GENISLETILEBILIR-CALISTIRMA-OLAYI.md): the
    /// consumer's own event does not fit any built-in <see cref="RunEventType"/>,
    /// so it writes <see cref="RunEventType.Custom"/> directly through the
    /// ambient <see cref="AgentPrismRunContext"/> — the same writer
    /// <c>RunRecordingAgent</c> uses for every built-in event. AgentPrism makes
    /// no claim about the payload's shape; it is entirely this tool's own.
    /// </remarks>
    [AgentPrismTool("mark_preview_ready", "Marks an order's preview as ready for the customer to review.")]
    public static async Task<string> MarkPreviewReady([Description("The order number.")] string orderId)
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

        return $"Preview for order {orderId} is ready to review.";
    }
}

/// <summary>
/// A supported object parameter (135.1): a public record with a single public
/// constructor. Every attribute below is applied directly to the positional
/// parameter — never with an explicit <c>[property: ...]</c> target, which the
/// generator does not read.
/// </summary>
public sealed record ShippingAddress(
    [Description("The street address.")] string Street,
    [Description("The city.")] string City,
    [Description("The postal code.")] string PostalCode);

[JsonSerializable(typeof(ShippingAddress))]
internal sealed partial class ShippingAddressJsonContext : JsonSerializerContext;
