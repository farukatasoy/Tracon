using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Tracon's built-in error classifier.
/// </summary>
/// <remarks>
/// <para>
/// First tries an exact match on the error's <strong>stable identity</strong>
/// (<see cref="RunError.Type"/>); this maps both the value written by
/// <see cref="TraconException"/> subtypes (for example: <c>content_filtered</c>)
/// and the old fully-qualified type name written before that value was added
/// (for example: <c>Tracon.TraconCompilationException</c>) to the SAME
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
    /// <summary>Creates a new instance of Tracon's built-in error classifier.</summary>
    public DefaultRunErrorClassifier()
    {
    }

    private static readonly Dictionary<string, RunErrorClass> StableIdentities = new(StringComparer.Ordinal)
    {
        [TraconContentFilteredException.ContentFilteredErrorType] = RunErrorClass.ContentFiltered,
        [TraconContentBlockedException.ContentBlockedErrorType] = RunErrorClass.ContentBlocked,
        [TraconCompilationException.CompilationFailedErrorType] = RunErrorClass.CompilationFailed,
        ["Tracon.TraconCompilationException"] = RunErrorClass.CompilationFailed,
        [TraconProviderUnavailableException.ProviderUnavailableErrorType] = RunErrorClass.ProviderUnavailable,
        ["Tracon.TraconProviderUnavailableException"] = RunErrorClass.ProviderUnavailable,
        [TraconToolTimeoutException.ToolTimeoutErrorType] = RunErrorClass.ToolTimeout,
        [TraconRunBudgetExceededException.RunBudgetExceededErrorType] = RunErrorClass.QuotaExceeded,
        [TraconStructuredResponseException.StructuredResponseInvalidErrorType] = RunErrorClass.StructuredResponseInvalid,

        // 🚨 The identity the normalizer stamps on EVERY foreign provider
        // failure. Without this entry it matched nothing: the normalizer
        // replaces the SDK's exception with a TraconException carrying a fixed
        // message, so neither the type patterns nor the message patterns below
        // could ever see the provider's own text. Measured on a real OpenAI 404
        // and a real OpenRouter 402 — both recorded as Unknown, which is the
        // one bucket the taxonomy exists to avoid.
        [ProviderFailureNormalizer.UpstreamErrorType] = RunErrorClass.ProviderError,

        // Not a provider fault: the run's tenant has a credential for a
        // provider whose adapter cannot take one, so the chat client is never
        // built. That is a configuration mismatch caught while assembling the
        // agent, which is what CompilationFailed records.
        [ProviderFailureNormalizer.CredentialUnsupportedErrorType] = RunErrorClass.CompilationFailed,
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

        // 🚨 The patterns below run on the exception's TYPE NAME and MESSAGE as
        // text, and a normalized provider failure reaches them with neither:
        // its identity is matched above instead. They still carry every foreign
        // exception that fails OUTSIDE the model-call boundary, where nothing
        // normalized it — that is the traffic they were measured against.
        //
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

    // 🚨 Measured (samples/Tracon.Api, a real OpenAI 404 response): the
    // official provider SDKs do NOT THROW HttpRequestException. OpenAI's
    // System.ClientModel-based client throws ClientResultException, and the
    // Azure SDKs throw RequestFailedException. The type pattern therefore also
    // covers SDK wrappers; the System.Net types (socket/IO) are only for
    // providers that use an HTTP client directly.
    //
    // 🚨 That measurement no longer describes the MODEL-CALL path. Since the
    // normalizer was added, a failure crossing that boundary arrives as
    // upstream_error and is matched by identity above — this pattern never sees
    // ClientResultException there any more. It still carries the same SDK types
    // when they escape somewhere the normalizer does not wrap, so it stays; the
    // claim that it is what catches a provider 404 does not.
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
