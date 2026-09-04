namespace AgentPrism;

/// <summary>The default <see cref="IToolApprovalPresenter"/>: resolves nothing.</summary>
/// <remarks>
/// Registered with <c>TryAddSingleton</c>, so a consumer's own registration always wins.
/// <see cref="ToolApprovalPresenterRunner"/> checks for this exact type and skips calling
/// it — no presentation was ever going to come back, so there is nothing to gain from
/// building a <see cref="ToolApprovalContext"/> and a timeout for every approval request.
/// </remarks>
internal sealed class NullToolApprovalPresenter : IToolApprovalPresenter
{
    public static readonly NullToolApprovalPresenter Instance = new();

    /// <inheritdoc />
    public ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<ToolApprovalPresentation?>(null);
}
