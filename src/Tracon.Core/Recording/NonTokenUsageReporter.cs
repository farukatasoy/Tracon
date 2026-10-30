namespace Tracon;

/// <summary>
/// Records one non-token item that code running inside a run reports — the shared body
/// behind <c>IWorkflowContext.ReportUsageAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// An item does two things and they happen in this order. It joins the run's total
/// (<see cref="NonTokenCostAccumulator"/>), which also tells the tree's budget; then it
/// is written to the run's detail. The total comes first on purpose: a store that
/// cannot write the item disables the run's recording, and the run's quota entry and
/// budget must still hear about money the provider already billed.
/// </para>
/// <para>
/// Observability does not break functionality: this never throws, and a refused item is
/// a <see langword="false"/> and a log line, not an exception in the node.
/// </para>
/// </remarks>
internal static class NonTokenUsageReporter
{
    /// <summary>Reports one item for the current run.</summary>
    /// <param name="nodeName">The reporting function node, or <see langword="null"/> when unknown.</param>
    /// <param name="usage">The item.</param>
    /// <returns>
    /// <see langword="true"/> when the item was recorded. <see langword="false"/> outside a
    /// run, for an invalid item, and when the run already reported the most items it can.
    /// </returns>
    public static async ValueTask<bool> ReportAsync(string? nodeName, ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (TraconRunContext.Current is not { Writer: { } writer, NonTokenCost: { } accumulator } scope)
        {
            return false;
        }

        if (NonTokenCostAccumulator.Validate(usage) is { } reason)
        {
            writer.LogNonTokenUsageRefused(nodeName, reason);

            return false;
        }

        switch (accumulator.AddNodeItem(usage))
        {
            case NonTokenAddStatus.Rejected:
                writer.LogNonTokenUsageRefused(
                    nodeName,
                    $"The run already reported {NonTokenCostAccumulator.MaxNodeItems} items, the most a run can; report one item per step, not per chunk.");

                return false;

            case NonTokenAddStatus.Late:
                writer.LogNonTokenUsageLate(nodeName);
                break;

            default:
                break;
        }

        // Not the caller's token, and not the run's: the work was done and billed.
        await writer.RecordNonTokenUsageAsync(
            new NonTokenUsageRecord
            {
                Id = TraconId.NewId(),
                RunId = scope.RunId,
                NodeName = nodeName,
                Usage = usage,
                CreatedAt = DateTimeOffset.UtcNow,

                // Expected tenant stamp.
                TenantId = writer.TenantId,
            },
            CancellationToken.None).ConfigureAwait(false);

        return true;
    }
}
