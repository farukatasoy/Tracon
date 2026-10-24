namespace Tracon;

/// <summary>
/// Whose quota counter a call tree's consumption is written to, and how.
/// </summary>
/// <remarks>
/// <para>
/// Every run in a tree writes its OWN consumption into the quota when it ends, under
/// the root's tenant and name. A background child ends after its root, in a task
/// whose ambient tenant and user are not the root's, so the identity cannot be read
/// where the child ends: the root states it once, here, and the tree's budget (the
/// one object every run in the tree already shares) carries it.
/// </para>
/// <para>
/// Created by whatever opens the tree's root: the agent run recorder or the workflow
/// runner. <see langword="null"/> on a budget no root of ours opened; a run with no
/// account writes nothing, since it has no counter to name.
/// </para>
/// </remarks>
/// <param name="TenantId">The tenant of the root run.</param>
/// <param name="RootName">The root's agent or workflow name: the counter every run of the tree adds to.</param>
/// <param name="UserId">The root's user, or <see langword="null"/> when the root carries no attribution.</param>
/// <param name="Quota">Where the entries go, or <see langword="null"/> when no quota is registered.</param>
internal sealed record TreeAccount(string TenantId, string RootName, string? UserId, IQuotaRecorder? Quota)
{
    /// <summary>
    /// Gets what the tree has written to the counter. Written by <c>TreeQuota.RecordAsync</c> alone, read by the
    /// budget to tell a quota check how much of the tree's spend the counter has not seen yet.
    /// </summary>
    public TreeLedger Ledger { get; } = new();
}
