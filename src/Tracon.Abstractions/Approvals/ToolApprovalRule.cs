namespace Tracon;

/// <summary>
/// A persistent approval rule for a tool call the user said "do not ask again" for.
/// </summary>
/// <remarks>
/// <para>
/// A rule is bound to the triple <strong>tenant + agent + tool</strong>. An approval
/// given by one tenant does not hold in another tenant; this is a security boundary and
/// is not relaxed.
/// </para>
/// <para>
/// When <c>ArgumentsHash</c> is populated the rule covers only a call made with
/// <em>the same arguments</em>. When it is empty the rule covers every call of the tool.
/// The distinction exists because Microsoft Agent Framework offers two separate "always
/// approve" forms: <c>CreateAlwaysApproveToolResponse</c> and
/// <c>CreateAlwaysApproveToolWithArgumentsResponse</c>.
/// </para>
/// <para>
/// <see cref="ArgumentConditions"/> is a third, admin-authored form: instead of an exact
/// argument fingerprint it carries a set of comparisons (for example "amount &lt;= 100")
/// evaluated on every call. It is mutually exclusive with <c>ArgumentsHash</c>.
/// </para>
/// </remarks>
public sealed record ToolApprovalRule
{
    /// <summary>Gets the rule id. A time-ordered UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant the rule holds in.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the agent the rule holds for. When <see langword="null"/> it covers every
    /// agent of the tenant.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the tool the rule holds for.</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the argument fingerprint. When it is populated the rule covers only a call
    /// made with the same arguments.
    /// </summary>
    public string? ArgumentsHash { get; init; }

    /// <summary>
    /// Gets the argument conditions. All conditions must match for the rule to apply
    /// (<c>AND</c>); an empty list matches every call of the tool. Mutually exclusive
    /// with <c>ArgumentsHash</c> — a rule carries one or the other, never both.
    /// </summary>
    public IReadOnlyList<ToolArgumentCondition> ArgumentConditions { get; init; } = [];

    /// <summary>Gets who created the rule, or <see langword="null"/> when there is no authentication.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>Gets the creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The store for persistent approval rules.</summary>
/// <remarks>
/// The rules are read on every run; implementations must keep the read cheap. The
/// in-memory implementation holds every rule in memory; the PostgreSQL implementation is
/// indexed on <c>(tenant_id, tool_name)</c>.
/// </remarks>
public interface IToolApprovalRuleStore
{
    /// <summary>Lists the rules of a tenant.</summary>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rules, newest first.</returns>
    ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a rule. When a rule with the same identity is already stored, the existing
    /// rule is returned and no new record is opened.
    /// </summary>
    /// <remarks>
    /// A rule's identity is its tenant, agent, tool, <see cref="ToolApprovalRule.ArgumentsHash"/>,
    /// and condition set. The condition set is compared as a set - the order of the
    /// conditions does not count. Within a condition, the path and the operator are
    /// compared exactly and the value as its JSON text, with one exception: a list value
    /// (<see cref="ToolArgumentOperator.In"/>, <see cref="ToolArgumentOperator.NotIn"/>) is
    /// compared element by element, so whitespace between its elements does not count.
    /// Every other value is compared as the exact JSON text it was written with -
    /// <c>100</c> and <c>100.0</c> are two different conditions, and so are two strings
    /// that differ only inside the quotes.
    /// </remarks>
    /// <param name="rule">The rule to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted rule.</returns>
    ValueTask<ToolApprovalRule> AddAsync(ToolApprovalRule rule, CancellationToken cancellationToken = default);

    /// <summary>Deletes a rule.</summary>
    /// <param name="tenantId">The tenant id. The rule of another tenant cannot be deleted.</param>
    /// <param name="ruleId">The rule id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the rule was deleted.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, Guid ruleId, CancellationToken cancellationToken = default);
}
