using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// AgentPrism's built-in error classifier.
/// </summary>
/// <remarks>
/// <para>
/// First tries an exact match on the error's <strong>stable identity</strong>
/// (<see cref="RunError.Type"/>); this maps both the value written by
/// <see cref="AgentPrismException"/> subtypes (for example: <c>content_filtered</c>)
/// and the old fully-qualified type name written before that value was added
/// (for example: <c>AgentPrism.AgentPrismCompilationException</c>) to the SAME
/// class — past records join the new taxonomy without breaking.
/// </para>
/// <para>
/// If there is no exact match, it falls back to pattern matching on the
/// message and the type name. An error that matches no rule becomes
/// <see cref="RunErrorClass.Unknown"/> — it is <strong>never guessed</strong>.
/// </para>
/// <para>
/// Called only on the error path (see <see cref="IRunErrorClassifier"/>); the
/// patterns are written with the source generator (<c>GeneratedRegex</c>) so
/// as not to allocate on the hot path.
/// </para>
/// <para>
/// Public, and safe to construct directly: it carries no dependencies, so a
/// consumer's own <see cref="IRunErrorClassifier"/> can compose it — try an
/// SDK-specific rule first, then fall back to
/// <c>builtIn.Classify(runError)</c> — without going through DI.
/// </para>
/// </remarks>
public sealed partial class DefaultRunErrorClassifier : IRunErrorClassifier
{
    /// <summary>Creates a new instance of AgentPrism's built-in error classifier.</summary>
    public DefaultRunErrorClassifier()
    {
    }

    private static readonly Dictionary<string, RunErrorClass> StableIdentities = new(StringComparer.Ordinal)
    {
        [AgentPrismContentFilteredException.ContentFilteredErrorType] = RunErrorClass.ContentFiltered,
        [AgentPrismContentBlockedException.ContentBlockedErrorType] = RunErrorClass.ContentBlocked,
        [AgentPrismCompilationException.CompilationFailedErrorType] = RunErrorClass.CompilationFailed,
        ["AgentPrism.AgentPrismCompilationException"] = RunErrorClass.CompilationFailed,
        [AgentPrismProviderUnavailableException.ProviderUnavailableErrorType] = RunErrorClass.ProviderUnavailable,
        ["AgentPrism.AgentPrismProviderUnavailableException"] = RunErrorClass.ProviderUnavailable,
        [AgentPrismToolTimeoutException.ToolTimeoutErrorType] = RunErrorClass.ToolTimeout,
        [AgentPrismRunBudgetExceededException.RunBudgetExceededErrorType] = RunErrorClass.QuotaExceeded,
        [AgentPrismStructuredResponseException.StructuredResponseInvalidErrorType] = RunErrorClass.StructuredResponseInvalid,
    };

    /// <inheritdoc />
    public RunErrorClassification Classify(RunError runError)
    {
        ArgumentNullException.ThrowIfNull(runError);

        return new RunErrorClassification
        {
            Class = ClassifyCore(runError),
            Fingerprint = ErrorFingerprint.Compute(runError.Message),
        };
    }

    private static RunErrorClass ClassifyCore(RunError runError)
    {
        if (StableIdentities.TryGetValue(runError.Type, out var stable))
        {
            return stable;
        }

        // 🚨 The timeout check runs BEFORE the cancelled-type check, and the
        // order is the whole point. TaskCanceledException is what HttpClient
        // raises on its own request timeout, so the TYPE alone cannot separate
        // "the caller pressed stop" from "the provider never answered"; the
        // message can, and a genuine cancellation's message ("A task was
        // canceled.") does not match the timeout pattern. Reversed, every
        // provider timeout was filed as Canceled and disappeared from the
        // failure dashboards. Measured in Phase 157.
        if (TimeoutPattern().IsMatch(runError.Type) || TimeoutPattern().IsMatch(runError.Message))
        {
            return RunErrorClass.Timeout;
        }

        if (CanceledTypePattern().IsMatch(runError.Type))
        {
            return RunErrorClass.Canceled;
        }

        if (RateLimitPattern().IsMatch(runError.Message) || RateLimitPattern().IsMatch(runError.Type))
        {
            return RunErrorClass.RateLimited;
        }

        if (QuotaPattern().IsMatch(runError.Message))
        {
            return RunErrorClass.QuotaExceeded;
        }

        if (ToolErrorPattern().IsMatch(runError.Message))
        {
            return RunErrorClass.ToolError;
        }

        if (ProviderErrorTypePattern().IsMatch(runError.Type) || ProviderErrorMessagePattern().IsMatch(runError.Message))
        {
            return RunErrorClass.ProviderError;
        }

        return RunErrorClass.Unknown;
    }

    [GeneratedRegex(
        @"(?:^|\.)(?:OperationCanceledException|TaskCanceledException)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CanceledTypePattern();

    [GeneratedRegex(
        @"timeoutexception|\btimed?[\s_-]?out\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimeoutPattern();

    [GeneratedRegex(
        @"\b429\b|toomanyrequests|\brate[\s_-]?limit(?:ed|ing)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RateLimitPattern();

    [GeneratedRegex(@"\bquota\b|\bkota\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex QuotaPattern();

    [GeneratedRegex(@"\btool\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ToolErrorPattern();

    // 🚨 Measured (samples/AgentPrism.Api, a real OpenAI 404 response): the
    // official provider SDKs do NOT THROW HttpRequestException. OpenAI's
    // System.ClientModel-based client throws ClientResultException, and the
    // Azure SDKs throw RequestFailedException. The type pattern therefore also
    // covers SDK wrappers; the System.Net types (socket/IO) are only for
    // providers that use an HTTP client directly.
    [GeneratedRegex(
        @"httprequestexception|socketexception|ioexception|clientresultexception|requestfailedexception|apiexception",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderErrorTypePattern();

    // Fallback signature: even if the type is not recognized, seeing "HTTP
    // 4xx"/"HTTP 5xx" in the message shows an HTTP error occurred on the
    // provider side (example: "HTTP 404 (invalid_request_error: model_not_found)").
    [GeneratedRegex(@"\bHTTP\s+[45]\d{2}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderErrorMessagePattern();
}
