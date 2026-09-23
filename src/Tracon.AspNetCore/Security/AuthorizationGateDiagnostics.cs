using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// The logging half of the HTTP authorization gates
/// (<see cref="RunAuthorizationGate"/> and <see cref="SessionOwnershipGate"/>):
/// reads the caller's identity and makes a consumer seam that failed visible.
/// </summary>
/// <remarks>
/// <para>
/// Every gate is fail-closed: a consumer seam that throws ends in a refusal.
/// A refusal alone is silent, though — the caller sees an ordinary denial and
/// the operator sees nothing at all. The failure is therefore always written
/// to the log, and never to the HTTP response, which must not carry the
/// consumer's exception text.
/// </para>
/// <para>
/// The logger is created only on a failure path, from the request's own
/// services, so the gates stay static and the happy path pays nothing.
/// </para>
/// </remarks>
internal static class AuthorizationGateDiagnostics
{
    /// <summary>The log category every authorization gate failure is written under.</summary>
    public const string RunAuthorizationCategory = "Tracon.RunAuthorization";

    /// <summary>The key that marks a request whose identity failure has already been written.</summary>
    private static readonly object AttributionFaultReportedKey = new();

    /// <summary>
    /// Reads the caller's user id for an authorization decision.
    /// </summary>
    /// <param name="attribution">The attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="httpContext">The request.</param>
    /// <returns>The user id, or <see langword="null"/> when there is no usable identity.</returns>
    /// <remarks>
    /// An identity that cannot be read is NO identity, never a guessed one —
    /// the fail-closed direction for every gate that compares it. The failure
    /// is written once per request: one request passes several gates, and each
    /// of them reads the same identity.
    /// </remarks>
    public static string? ReadUserId(IRunAttributionContext? attribution, HttpContext httpContext)
    {
        try
        {
            var (userId, _) = RunAttributionReader.Read(
                attribution,
                (message, exception) => ReportAttributionFault(httpContext, message, exception));

            return userId;
        }
        catch (OperationCanceledException exception) when (OperationCancellation.IsFailure(exception, httpContext.RequestAborted))
        {
            // The reader lets every cancellation through, but only the caller's
            // own abort is a cancellation here. A consumer attribution whose
            // own call timed out is a failed identity, the same as any other
            // fault; letting it escape would turn a denial into a 500.
            ReportAttributionFault(
                httpContext,
                $"The registered {nameof(IRunAttributionContext)} threw a cancellation the request did not ask for.",
                exception);

            return null;
        }
    }

    /// <summary>Creates a logger from the request's services.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="category">The log category.</param>
    /// <returns>The logger, or a logger that writes nothing when no logging is registered.</returns>
    public static ILogger CreateLogger(HttpContext httpContext, string category)
        => httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(category) ?? NullLogger.Instance;

    private static void ReportAttributionFault(HttpContext httpContext, string message, Exception? exception)
    {
        if (!httpContext.Items.TryAdd(AttributionFaultReportedKey, true))
        {
            return;
        }

        var logger = CreateLogger(httpContext, RunAuthorizationCategory);

        if (exception is null)
        {
            // The implementation answered, but with a value that breaks a limit.
            logger.LogWarning(
                "{Message} The authorization checks of this request treat the caller as having no identity.",
                message);
        }
        else
        {
            logger.LogError(
                exception,
                "{Message} The authorization checks of this request treat the caller as having no identity.",
                message);
        }
    }
}
