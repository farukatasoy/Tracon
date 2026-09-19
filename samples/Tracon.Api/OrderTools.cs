using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Tracon.Api;

/// <summary>
/// Sample tools. A real application would call a database or a service here.
/// </summary>
/// <remarks>
/// Methods are marked with <c>[TraconTool]</c> and registered with
/// <c>builder.AddTracon().AddToolsFrom(typeof(OrderTools))</c>. An unmarked
/// method is not a tool — adding a new helper method to this class does not
/// automatically expose it to agents. The UI only lets the user pick from this
/// list; it never generates tool code.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Returns the shipping status of an order.</summary>
    /// <param name="orderId">The order number.</param>
    /// <returns>The shipping status text.</returns>
    [TraconTool("get_order_status", "Returns the shipping status of an order.")]
    public static string GetOrderStatus([Description("The order number.")] string orderId)
        => $"Order {orderId} has shipped. Estimated delivery: 2 days.";

    /// <summary>Lists a customer's recent orders.</summary>
    /// <param name="customerId">The customer number.</param>
    /// <returns>The order list text.</returns>
    [TraconTool("list_recent_orders", "Lists a customer's recent orders.")]
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
    [TraconTool(
        "cancel_order",
        "Cancels an order.",
        RequiresApproval = true,
        Effect = ToolEffect.Destructive,
        RequiredPermission = "orders.cancel")]
    public static string CancelOrder([Description("The order number.")] string orderId)
        => $"Order {orderId} has been canceled.";

    /// <summary>Refunds part or all of an order.</summary>
    /// <param name="orderId">The order number.</param>
    /// <param name="amount">The amount to refund, in the order's currency.</param>
    /// <returns>The refund result text.</returns>
    /// <remarks>
    /// 🚨 The one approval-requiring tool in this sample that takes a NUMERIC
    /// argument, and it is here for that reason. A conditional approval rule
    /// (<c>ToolApprovalRule.ArgumentConditions</c>, for example
    /// <c>amount &lt;= 100</c>) can only be demonstrated against an argument it
    /// can compare; <c>cancel_order</c> carries an order id and nothing to
    /// threshold. Without this tool the whole conditional-rule feature is
    /// reachable through the management API but never OBSERVABLE in a run.
    /// </remarks>
    [TraconTool(
        "refund_order",
        "Refunds part or all of an order.",
        RequiresApproval = true,
        Effect = ToolEffect.Destructive,
        RequiredPermission = "orders.refund")]
    public static string RefundOrder(
        [Description("The order number.")] string orderId,
        [Description("The amount to refund, in the order's currency.")] decimal amount)
        => $"Refunded {amount} for order {orderId}.";

    /// <summary>
    /// Demo tool for F-114 (docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md): its
    /// body sleeps far longer than its own 1-second timeout, showing that the
    /// registry's <c>TimeoutAIFunction</c> wrapper cuts the WAIT short — the
    /// body itself keeps running in the background, a documented limit of
    /// cooperative cancellation (Manual Case 8).
    /// </summary>
    /// <param name="reportId">The report number.</param>
    /// <returns>The report text — never actually reached at the default timeout.</returns>
    [TraconTool("get_slow_report", "Fetches a report that is slow to generate.", TimeoutSeconds = 1)]
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
    [TraconTool(
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
    /// ambient <see cref="TraconRunContext"/> — the same writer
    /// <c>RunRecordingAgent</c> uses for every built-in event. Tracon makes
    /// no claim about the payload's shape; it is entirely this tool's own.
    /// </remarks>
    [TraconTool("mark_preview_ready", "Marks an order's preview as ready for the customer to review.")]
    public static async Task<string> MarkPreviewReady([Description("The order number.")] string orderId)
    {
        var writer = TraconRunContext.Current?.Writer;

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

    /// <summary>Reports which user the current run is attributed to.</summary>
    /// <returns>The attributed user id, or a sentence saying there is none.</returns>
    /// <remarks>
    /// 🚨 Demonstration tool. It exists so the run attribution seam can be
    /// OBSERVED from inside a tool: <see cref="IRunAttributionContext"/> is read
    /// once, when the run opens, and the value is carried on the ambient
    /// <see cref="TraconRunContext"/> for the rest of it. Without a tool that
    /// reads it back, nothing in this reference application shows that the
    /// identity actually reaches tool code — the run record alone only proves
    /// the recorder saw it.
    /// </remarks>
    [TraconTool("whoami", "Reports which user the current run is attributed to.")]
    public static string WhoAmI() =>
        TraconRunContext.Current?.UserId is { Length: > 0 } userId
            ? $"This run is attributed to '{userId}'."
            : "This run carries no attributed user.";
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
