namespace Tracon.Api;

/// <summary>
/// Resolves the customer name behind an order id, so the <c>cancel_order</c>
/// approval card reads "Cancel order for Priya Shah" instead of a bare
/// <c>{ "orderId": "ORD-1001" }</c>.
/// </summary>
/// <remarks>
/// A real application would look this up in its own order database through an
/// injected <c>IServiceScopeFactory</c> (see <see cref="IToolApprovalPresenter"/>'s
/// own <c>&lt;example&gt;</c>) — this sample has no database, so the "lookup" is a
/// fixed table matching the two order ids <see cref="OrderTools"/> already talks
/// about (<c>ORD-1001</c>, <c>ORD-1002</c>). An unknown order id returns
/// <see langword="null"/>: per <see cref="IToolApprovalPresenter"/>'s fail-open
/// contract, the approval request is still published, just without a resolved
/// name — the raw <c>orderId</c> argument stays visible either way.
/// </remarks>
internal sealed class OrderApprovalPresenter : IToolApprovalPresenter
{
    private static readonly Dictionary<string, string> CustomerNamesByOrderId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORD-1001"] = "Priya Shah",
        ["ORD-1002"] = "Marcus Lee",
    };

    /// <inheritdoc />
    public ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.ToolName, "cancel_order", StringComparison.Ordinal))
        {
            return ValueTask.FromResult<ToolApprovalPresentation?>(null);
        }

        if (context.GetString("orderId") is not { } orderId ||
            !CustomerNamesByOrderId.TryGetValue(orderId, out var customerName))
        {
            return ValueTask.FromResult<ToolApprovalPresentation?>(null);
        }

        return ValueTask.FromResult<ToolApprovalPresentation?>(new ToolApprovalPresentation
        {
            EntityType = "order",
            EntityId = orderId,
            EntityName = $"Order {orderId}",
            Message = $"Cancel order {orderId} for {customerName}.",
        });
    }
}
