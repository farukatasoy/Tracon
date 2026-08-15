using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Applies persistent approval rules to a tool call.
/// </summary>
/// <remarks>
/// <para>
/// Integrates with Microsoft Agent Framework's <c>ToolApprovalAgentOptions.AutoApprovalRules</c>
/// API. When a rule matches, the call runs without asking the user.
/// </para>
/// <para>
/// <strong>This class protects the tenant boundary.</strong> It always reads rules
/// with <see cref="ITenantContext.TenantId"/>. An approval from one tenant cannot
/// run a call for another tenant.
/// </para>
/// <para>
/// <strong>A store error does not grant approval.</strong> If a rule cannot be read,
/// the call is not automatically approved and the system asks the user. This is the safe default.
/// </para>
/// </remarks>
public sealed class ToolApprovalRuleEvaluator
{
    private readonly IToolApprovalRuleStore _rules;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ToolApprovalRuleEvaluator> _logger;

    /// <summary>Initializes a new evaluator.</summary>
    /// <param name="rules">The rule store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ToolApprovalRuleEvaluator(
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        ILogger<ToolApprovalRuleEvaluator> logger)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        _rules = rules;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether a persistent rule automatically approves a tool call.
    /// </summary>
    /// <param name="agentName">The agent that makes the call.</param>
    /// <param name="call">The tool call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the call is automatically approved.</returns>
    public async ValueTask<bool> IsAutoApprovedAsync(
        string agentName,
        FunctionCallContent call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        var tenantId = _tenantContext.TenantId;

        IReadOnlyList<ToolApprovalRule> rules;

        try
        {
            rules = await _rules.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not read approval rules; the call to '{ToolName}' will require approval.",
                call.Name);

            return false;
        }

        if (rules.Count == 0)
        {
            return false;
        }

        string? argumentsHash = null;

        foreach (var rule in rules)
        {
            if (!string.Equals(rule.ToolName, call.Name, StringComparison.Ordinal))
            {
                continue;
            }

            // If AgentName is empty, the rule applies to every agent in the tenant.
            if (rule.AgentName is { } scoped && !string.Equals(scoped, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (rule.ArgumentsHash is null)
            {
                return true;
            }

            argumentsHash ??= ComputeArgumentsHash(call.Arguments);

            if (string.Equals(rule.ArgumentsHash, argumentsHash, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Produces a deterministic fingerprint from tool arguments.
    /// </summary>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The hexadecimal fingerprint, or an empty string when there are no arguments.</returns>
    /// <remarks>
    /// <para>
    /// Keys are sorted. Dictionary order can change between runs, and a different
    /// fingerprint for the same call would prevent a "do not ask again" rule from matching.
    /// </para>
    /// <para>
    /// This method does <em>not</em> use JSON serialization. Reflection-based serialization
    /// produces <c>IL2026</c>, and <c>AgentPrism.Core</c> is marked as AOT-compatible.
    /// </para>
    /// </remarks>
    public static string ComputeArgumentsHash(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var pair in arguments.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key)
                .Append('=')
                .Append(Convert.ToString(pair.Value, CultureInfo.InvariantCulture))
                // The separator is the unit separator (U+001F). It is not present in text
                // values, so different dictionaries cannot produce the same fingerprint.
                .Append('\u001F');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash);
    }
}
