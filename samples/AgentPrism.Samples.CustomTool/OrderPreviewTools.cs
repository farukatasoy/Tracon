using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgentPrism.Samples.CustomTool;

/// <summary>Read-only tools generated at compile time.</summary>
public static class OrderPreviewTools
{
    /// <summary>Returns a structured preview for an order.</summary>
    [AgentPrismTool(
        "preview_order",
        "Returns a structured order preview.",
        MaxOutputBytes = 768,
        JsonSerializerContext = typeof(OrderPreviewJsonContext))]
    public static OrderPreview PreviewOrder([Description("The order number.")] string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return new OrderPreview(orderId, "ready");
    }
}

/// <summary>The structured result of a generated preview tool.</summary>
public sealed record OrderPreview(string OrderId, string Status);

[JsonSerializable(typeof(OrderPreview))]
internal sealed partial class OrderPreviewJsonContext : JsonSerializerContext;
