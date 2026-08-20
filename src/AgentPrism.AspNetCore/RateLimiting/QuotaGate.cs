using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// Helper that checks the quota before a run starts and produces a
/// <c>429</c> when it is exceeded.
/// </summary>
/// <remarks>
/// <para>
/// The check is called <strong>explicitly</strong> at every endpoint that starts a
/// run; it is not an endpoint filter. On OpenAI-compatible endpoints, the
/// agent name is not in the route value but in the body's <c>model</c> field, and a
/// filter reading the body would require parsing the request twice.
/// </para>
/// <para>
/// An ongoing run is <strong>not cut off</strong> when the quota is exceeded.
/// This gate only stops a <em>new</em> run.
/// </para>
/// </remarks>
internal static class QuotaGate
{
    /// <summary>Checks the quota; produces the response to return if it is exceeded.</summary>
    /// <param name="enforcer">The quota enforcer. If <see langword="null"/>, no check is performed.</param>
    /// <param name="tenants">The tenant context.</param>
    /// <param name="agentName">The name of the agent to run.</param>
    /// <param name="httpContext">The request context. The <c>Retry-After</c> header is written here.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The <c>429</c> response to return if the quota is exceeded; otherwise
    /// <see langword="null"/>.
    /// </returns>
    public static async ValueTask<IResult?> CheckAsync(
        QuotaEnforcer? enforcer,
        ITenantContext tenants,
        string agentName,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (enforcer is null)
        {
            return null;
        }

        var decision = await enforcer
            .CheckAsync(tenants.TenantId, agentName, cancellationToken)
            .ConfigureAwait(false);

        if (decision.IsAllowed)
        {
            return null;
        }

        // If it is known when the counter resets, that is given to the client as
        // Retry-After; the client should not have to guess.
        if (decision.ResetsAt is { } resetsAt)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((resetsAt - DateTimeOffset.UtcNow).TotalSeconds));

            httpContext.Response.Headers.RetryAfter =
                seconds.ToString(CultureInfo.InvariantCulture);
        }

        return Results.Problem(
            title: "Quota exceeded",
            detail: decision.Reason ?? "The quota defined for this tenant has been exceeded.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: BuildExtensions(decision));
    }

    private static Dictionary<string, object?> BuildExtensions(QuotaDecision decision)
    {
        // The ProblemDetails extensions must be machine-readable: the client should
        // be able to answer "which quota, how much, when does it reset" without
        // parsing the text.
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["quotaMetric"] = decision.Metric?.ToString(),
            ["quotaPeriod"] = decision.Period?.ToString(),
            ["quotaLimit"] = decision.Limit,
            ["quotaUsed"] = decision.Used,
        };

        if (decision.AgentName is { Length: > 0 } agentName)
        {
            extensions["quotaAgentName"] = agentName;
        }

        if (decision.ResetsAt is { } resetsAt)
        {
            extensions["quotaResetsAt"] = resetsAt.ToString("O", CultureInfo.InvariantCulture);
        }

        if (decision.CostFellBackToTokens)
        {
            extensions["quotaCostFellBackToTokens"] = true;
        }

        return extensions;
    }
}
