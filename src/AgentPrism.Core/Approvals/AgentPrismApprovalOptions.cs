namespace AgentPrism;

/// <summary>Options for asynchronous approvals.</summary>
/// <remarks>
/// Reads values from the <c>AgentPrism:Approvals</c> configuration section. See
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>.
/// </remarks>
public sealed class AgentPrismApprovalOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:Approvals";

    /// <summary>
    /// A pending approval request has <see cref="ApprovalStatus.Expired"/> status
    /// after this duration.
    /// </summary>
    /// <remarks>
    /// The default is 24 hours. An approval often needs a person, and a short
    /// limit, such as one hour, is not enough for an overnight shift.
    /// </remarks>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets a value that enables the background scan that closes expired requests.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="true"/>. Unlike <see cref="RunReconciliationOptions"/>,
    /// this is a security requirement, not a maintenance convenience. An approval
    /// request that waits forever is a leak. See <c>IPendingApprovalStore.ExpireAsync</c>.
    /// </remarks>
    public bool ExpirationEnabled { get; set; } = true;

    /// <summary>The delay between expiration scans.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The maximum number of requests to close in one scan.</summary>
    public int MaxPerScan { get; set; } = 100;
}
