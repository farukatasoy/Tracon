using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Normalizes foreign provider failures at the outer model-call boundary.</summary>
internal static partial class ProviderFailureNormalizer
{
    internal const string UpstreamErrorType = "upstream_error";
    internal const string CredentialUnsupportedErrorType = "provider_credential_unsupported";
    internal const string UpstreamMessage = "The model provider request failed.";
    internal const string CredentialUnsupportedMessage = "The model provider does not support tenant credentials.";

    internal static bool ShouldNormalize(Exception exception)
        => exception is not OperationCanceledException && !IsKnownSafe(exception);

    /// <summary>
    /// Builds the persisted message for a masked provider failure: the fixed
    /// sentence plus the few facts that tell one fault from another.
    /// </summary>
    /// <param name="provider">The provider name from the model binding.</param>
    /// <param name="faultType">The foreign exception's type NAME — a class name, never its message.</param>
    /// <param name="status">The HTTP status the provider reported, when the failure carried one.</param>
    /// <remarks>
    /// <para>
    /// This text is the ONLY input the run record's fingerprint is computed
    /// from (<c>DefaultRunErrorClassifier.Classify</c>). While it was the fixed
    /// sentence alone, every foreign provider failure produced the SAME
    /// fingerprint: measured on real providers, an OpenAI 404 (model not found)
    /// and an OpenRouter 402 (out of credit) matched character for character.
    /// A fingerprint exists to separate different faults and cluster repeated
    /// ones; that one did neither.
    /// </para>
    /// <para>
    /// What goes in is bounded on purpose: a provider name Tracon chose, a type
    /// name, and a three-digit status. The provider's own message never does —
    /// it can quote the request, an internal URL, a host:port or a partial
    /// credential, and a run record is permanent. The full exception still
    /// reaches <c>ILogger</c> at the call site through <see cref="Log"/>.
    /// </para>
    /// </remarks>
    internal static string DescribeUpstreamFailure(string provider, string faultType, int? status)
    {
        var suffix = status is { } code ? $" (HTTP {code})" : string.Empty;

        return $"{UpstreamMessage} Provider: '{provider}', fault: '{faultType}'{suffix}.";
    }

    /// <summary>
    /// Reads the HTTP status a provider failure reports, from anywhere in the
    /// exception graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same approach <c>FallbackRetryClassifier</c> already takes, and for
    /// the same reason: <c>Tracon.Core</c> does not reference any provider
    /// SDK's exception types, so a status is recognized as TEXT rather than
    /// through a typed catch. Only the three digits are read; nothing else from
    /// the message is kept.
    /// </para>
    /// <para>
    /// A failure that never received a response (connection refused, DNS,
    /// stream reset) carries no status at all, and none is invented. The type
    /// name alone separates those — which is exactly how
    /// <c>FallbackRetryClassifier</c> treats the same case.
    /// </para>
    /// </remarks>
    internal static int? ReadStatus(Exception exception)
    {
        for (var candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (ReadStatus(inner) is { } nested)
                    {
                        return nested;
                    }
                }
            }

            var match = HttpStatusPattern().Match(candidate.Message);

            if (match.Success && int.TryParse(match.Groups["status"].ValueSpan, CultureInfo.InvariantCulture, out var status))
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>Names the foreign exception that actually reached the boundary.</summary>
    /// <remarks>
    /// An SDK's own retry pipeline wraps the real failure in an
    /// <see cref="AggregateException"/>, whose type name says nothing about the
    /// fault. The innermost frame is the one worth naming.
    /// </remarks>
    internal static string ReadFaultType(Exception exception)
    {
        var candidate = exception;

        while (candidate is AggregateException { InnerExceptions.Count: > 0 } aggregate)
        {
            candidate = aggregate.InnerExceptions[0];
        }

        while (candidate.InnerException is { } inner && candidate is AggregateException)
        {
            candidate = inner;
        }

        return candidate.GetType().Name;
    }

    // 🚨 Kept in step with FallbackRetryClassifier's RetryableHttpStatusPattern:
    // that one asks "is this worth retrying" and matches only 5xx/429, this one
    // asks "which fault was this" and needs every status. A status this reads
    // but that one does not is fine; the reverse would mean a retryable failure
    // with no discriminator.
    [GeneratedRegex(
        @"\bHTTP\s+(?<status>[1-5]\d{2})\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex HttpStatusPattern();

    internal static void Log(
        ILoggerFactory? loggerFactory,
        ModelBinding binding,
        Exception exception,
        string operation)
    {
        loggerFactory?
            .CreateLogger("Tracon.ModelProvider")
            .LogError(
                exception,
                "Model provider {Provider} failed during {Operation} for model {Model}.",
                binding.Provider,
                operation,
                binding.Model);
    }

    private static bool IsKnownSafe(Exception exception)
        => exception is TraconContentFilteredException
            or TraconContentBlockedException
            or TraconCompilationException
            or TraconProviderUnavailableException
            or TraconToolTimeoutException
            or TraconSessionConflictException
            or TraconExternalCallException
            or ReplayToolMismatchException
            or TraconAgentSourceException
            or JobRetryException
            or ProviderInvocationException
            // Internal and unforgeable (Tracon.Abstractions grants Tracon.Core
            // sole InternalsVisibleTo access): thrown only by Tracon's own
            // ModelProviderSettings.Validate, never by a foreign provider.
            or ProviderSettingsValidationException;
}

/// <summary>Marks an exception that crossed the raw provider boundary.</summary>
internal sealed class ForeignProviderInvocationException(Exception original) : Exception(original.Message, original)
{
    internal Exception Original { get; } = original;
}

/// <summary>Tags only failures thrown by the provider client itself.</summary>
internal sealed class ProviderFailureTaggingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
        {
            throw new ForeignProviderInvocationException(exception);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate> enumerator;

        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
        {
            throw new ForeignProviderInvocationException(exception);
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (ProviderFailureNormalizer.ShouldNormalize(exception))
                {
                    throw new ForeignProviderInvocationException(exception);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }
}

/// <summary>Internal stable provider failure carried to public protocol adapters.</summary>
internal sealed class ProviderInvocationException : TraconException
{
    private ProviderInvocationException(string errorType, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
    }

    public override string ErrorType { get; }

    internal static ProviderInvocationException UpstreamFailure(Exception exception, string provider)
        => new(
            ProviderFailureNormalizer.UpstreamErrorType,
            ProviderFailureNormalizer.DescribeUpstreamFailure(
                provider,
                ProviderFailureNormalizer.ReadFaultType(exception),
                ProviderFailureNormalizer.ReadStatus(exception)),
            exception);

    internal static ProviderInvocationException CredentialUnsupported(string providerName)
        => new(
            ProviderFailureNormalizer.CredentialUnsupportedErrorType,
            $"{ProviderFailureNormalizer.CredentialUnsupportedMessage} Provider: '{providerName}'.");
}

/// <summary>Preserves internal retry semantics, then masks the final provider failure.</summary>
internal sealed class ProviderFailureNormalizingChatClient(
    ModelBinding binding,
    IChatClient inner,
    ILoggerFactory? loggerFactory)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ForeignProviderInvocationException exception)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "invocation");
            throw ProviderInvocationException.UpstreamFailure(exception.Original, binding.Provider);
        }
        catch (TraconProviderUnavailableException exception)
            when (FindForeignFailure(exception) is { } foreign)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, foreign.Original, "invocation");
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate> enumerator;

        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (ForeignProviderInvocationException exception)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "streaming invocation");
            throw ProviderInvocationException.UpstreamFailure(exception.Original, binding.Provider);
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (ForeignProviderInvocationException exception)
                {
                    ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "streaming invocation");
                    throw ProviderInvocationException.UpstreamFailure(exception.Original, binding.Provider);
                }
                catch (TraconProviderUnavailableException exception)
                    when (FindForeignFailure(exception) is { } foreign)
                {
                    ProviderFailureNormalizer.Log(loggerFactory, binding, foreign.Original, "streaming invocation");
                    throw;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }

    private static ForeignProviderInvocationException? FindForeignFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is ForeignProviderInvocationException foreign)
            {
                return foreign;
            }
        }

        return null;
    }
}
