namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// Resolves the one order id the E2E fixtures ever use (<c>ORD-7</c>), so the
/// approvals screenshot shows a resolved name instead of a bare argument dump
/// (phase 142).
/// </summary>
internal sealed class ScriptedApprovalPresenter : IToolApprovalPresenter
{
    public ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default)
    {
        if (context.GetString("orderId") is not { } orderId)
        {
            return ValueTask.FromResult<ToolApprovalPresentation?>(null);
        }

        return ValueTask.FromResult<ToolApprovalPresentation?>(new ToolApprovalPresentation
        {
            EntityType = "order",
            EntityId = orderId,
            EntityName = $"Order {orderId}",
            Message = $"Cancel order {orderId} for Jordan Rivera.",
        });
    }
}
