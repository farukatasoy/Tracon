using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
/// <para>
/// <strong>A code-defined policy runs before the data rules and can
/// override them.</strong> Code is a security boundary; data — writable from the
/// UI — is not allowed to loosen it. See <see cref="ToolApprovalContext"/> and
/// <c>ITraconBuilder.AddToolApprovalPolicy(...)</c>.
/// </para>
/// </remarks>
public sealed class ToolApprovalRuleEvaluator
{
    private readonly IToolApprovalRuleStore _rules;
    private readonly ToolApprovalPolicyRegistry _policies;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ToolApprovalRuleEvaluator> _logger;

    /// <summary>Initializes a new evaluator.</summary>
    /// <param name="rules">The rule store.</param>
    /// <param name="policies">The code-defined policy registry.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ToolApprovalRuleEvaluator(
        IToolApprovalRuleStore rules,
        ToolApprovalPolicyRegistry policies,
        ITenantContext tenantContext,
        ILogger<ToolApprovalRuleEvaluator> logger)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        _rules = rules;
        _policies = policies;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether a call is auto-approved: either a code-defined policy decides
    /// so, or — when the policy is silent — a persistent data rule matches.
    /// </summary>
    /// <param name="agentName">The agent that makes the call.</param>
    /// <param name="call">The tool call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the call is automatically approved.</returns>
    /// <remarks>
    /// Code runs first and can override data in both directions: a
    /// <see cref="ToolApprovalPolicyDecision.Required"/> policy forces approval even if a
    /// data rule would otherwise auto-approve the call, and
    /// <see cref="ToolApprovalPolicyDecision.NotRequired"/> auto-approves even with no
    /// matching data rule. Only <see cref="ToolApprovalPolicyDecision.Undecided"/> (or no
    /// registered policy) falls through to the data rules.
    /// </remarks>
    public async ValueTask<bool> IsAutoApprovedAsync(
        string agentName,
        FunctionCallContent call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        var tenantId = _tenantContext.TenantId;

        if (_policies.TryGet(call.Name, out var policy))
        {
            var decision = EvaluatePolicy(policy, tenantId, agentName, call);

            switch (decision)
            {
                case ToolApprovalPolicyDecision.Required:
                    return false;
                case ToolApprovalPolicyDecision.NotRequired:
                    return true;
                case ToolApprovalPolicyDecision.Undecided:
                default:
                    break;
            }
        }

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
        IReadOnlyDictionary<string, object?>? arguments = null;

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

            if (rule.ArgumentsHash is { } hash)
            {
                argumentsHash ??= ComputeArgumentsHash(call.Arguments);

                if (string.Equals(hash, argumentsHash, StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (rule.ArgumentConditions.Count > 0)
            {
                arguments ??= ToArguments(call.Arguments);

                if (ToolArgumentConditionMatcher.Matches(rule.ArgumentConditions, arguments))
                {
                    return true;
                }

                continue;
            }

            // Neither an argument fingerprint nor conditions: the rule matches every call.
            return true;
        }

        return false;
    }

    private ToolApprovalPolicyDecision EvaluatePolicy(
        Func<ToolApprovalContext, ToolApprovalPolicyDecision> policy,
        string tenantId,
        string agentName,
        FunctionCallContent call)
    {
        try
        {
            return policy(new ToolApprovalContext
            {
                TenantId = tenantId,
                ToolName = call.Name,
                AgentName = agentName,
                Arguments = ToArguments(call.Arguments),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "The approval policy for tool '{ToolName}' threw; the call will require approval.",
                call.Name);

            return ToolApprovalPolicyDecision.Required;
        }
    }

    private static IReadOnlyDictionary<string, object?> ToArguments(IDictionary<string, object?>? arguments)
        => FunctionCallArguments.ToReadOnly(arguments);

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
    /// Each field is written <strong>length-prefixed</strong>, so no value can
    /// be read as a field boundary and two different argument sets cannot
    /// produce one fingerprint. This is what lets a grant mean "this exact
    /// call" and nothing wider.
    /// </para>
    /// <para>
    /// This method does <em>not</em> use JSON serialization. Reflection-based serialization
    /// produces <c>IL2026</c>, and <c>Tracon.Core</c> is marked as AOT-compatible.
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
            // 🚨 Length-prefixed, not separator-delimited. The previous format
            // joined fields with the unit separator (U+001F) on the stated
            // assumption that "it is not present in text values" -- an
            // assumption nothing enforced, while a JSON argument can carry any
            // character. A collision was constructed by hand: {"a":"x","b":"y"}
            // and {"a":"x<US>b=y"} hashed the same, so a grant for one call
            // could be inherited by a call of a different SHAPE. The path is the
            // script dispatcher, the surface that runs code.
            //
            // Writing each field as <byte length>:<value> removes the
            // assumption instead of documenting it: a value can no longer be
            // read as a field boundary whatever it contains.
            AppendLengthPrefixed(builder, pair.Key);
            AppendLengthPrefixed(builder, Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Writes one field as its UTF-8 byte length, a colon, then the value.
    /// </summary>
    /// <param name="builder">The buffer being hashed.</param>
    /// <param name="value">The field; <see langword="null"/> is written as an empty field.</param>
    /// <remarks>
    /// The length counts <strong>UTF-8 bytes</strong>, the same encoding the
    /// buffer is hashed in. A character count would let two values of equal
    /// length in characters but different length in bytes agree on the prefix.
    /// </remarks>
    private static void AppendLengthPrefixed(StringBuilder builder, string? value)
    {
        value ??= string.Empty;

        builder.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value);
    }
}
