using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Normalizes foreign provider failures at the outer model-call boundary.</summary>
internal static partial class ProviderFailureNormalizer
{
    internal const string UpstreamErrorType = TraconErrorTypes.UpstreamError;
    internal const string CredentialUnsupportedErrorType = TraconErrorTypes.ProviderCredentialUnsupported;
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

            if (ReadFrameStatus(candidate) is { } status)
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>Reads the HTTP status one frame's message reports, without looking at its inner exceptions.</summary>
    /// <param name="frame">One exception of a graph.</param>
    /// <returns>The status, or <see langword="null"/> when the message carries none.</returns>
    /// <remarks>
    /// Four shapes, each measured against the shipped SDK with an empty
    /// error body (<c>ProviderErrorStatusTests</c>): OpenAI's own body gives
    /// "HTTP 404 (...)"; System.ClientModel without one gives "Status: 404 (Not
    /// Found)" (OpenAI and Azure OpenAI); the Anthropic SDK gives "Status Code:
    /// NotFound", an <see cref="System.Net.HttpStatusCode"/> NAME; Google.GenAI gives
    /// "Request failed with status code 404: Not Found". Only "HTTP nnn" was read,
    /// so the other three looked like a failure with no response at all.
    /// </remarks>
    internal static int? ReadFrameStatus(Exception frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var match = HttpStatusPattern().Match(frame.Message);

        if (!match.Success)
        {
            return null;
        }

        if (match.Groups["status"] is { Success: true } digits
            && int.TryParse(digits.ValueSpan, CultureInfo.InvariantCulture, out var status))
        {
            return status;
        }

        return Enum.TryParse<System.Net.HttpStatusCode>(match.Groups["name"].Value, ignoreCase: false, out var named)
               && (int)named is >= 100 and <= 599
            ? (int)named
            : null;
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

    // The single status pattern: FallbackRetryClassifier, the circuit breaker
    // and the workflow node retry read a status through ProviderHttpStatus,
    // which comes here, so no second pattern can drift from this one.
    // The shapes are listed on ReadFrameStatus.
    [GeneratedRegex(
        @"\bHTTP\s+(?<status>[1-5]\d{2})\b|\bStatus:\s*(?<status>[1-5]\d{2})\b|\bstatus code\s+(?<status>[1-5]\d{2})\b|\bStatus Code:\s*(?:(?<status>[1-5]\d{2})\b|(?<name>[A-Za-z]+)\b)",
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
            or TraconOutputTruncatedException
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
            or TraconTenantProviderRefusedException
            // Internal and unforgeable (Tracon.Abstractions grants Tracon.Core
            // sole InternalsVisibleTo access): thrown only by Tracon's own
            // ModelProviderSettings.Validate, never by a foreign provider.
            or ProviderSettingsValidationException;
}

/// <summary>Marks an exception that crossed the raw provider boundary.</summary>
internal sealed class ForeignProviderInvocationException(Exception original) : Exception(original.Message, original)
{
    internal Exception Original { get; } = original;

    /// <summary>Finds the provider-boundary tag anywhere in an exception graph.</summary>
    /// <param name="exception">The failure to search.</param>
    /// <returns>The tag, or <see langword="null"/> when no provider client threw this failure.</returns>
    /// <remarks>
    /// Two readers ask this question: the normalizer, which masks only the
    /// provider's own failures, and the circuit breaker, which counts only them.
    /// One walk serves both, so they cannot disagree about where a failure came
    /// from.
    /// </remarks>
    internal static ForeignProviderInvocationException? Find(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ForeignProviderInvocationException foreign)
            {
                return foreign;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (Find(inner) is { } nested)
                    {
                        return nested;
                    }
                }
            }
        }

        return null;
    }
}

/// <summary>Tags only failures thrown by the provider client itself.</summary>
/// <remarks>
/// A cancellation keeps its type, so a caller's own cancellation still reads as
/// one; the client's own deadline is marked in <see cref="Exception.Data"/>
/// instead (<see cref="IsProviderCancellation"/>).
/// </remarks>
internal sealed class ProviderFailureTaggingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private const string ProviderCancellationKey = "Tracon.ProviderCancellation";

    /// <summary>
    /// Determines whether the provider client raised <paramref name="exception"/>
    /// itself without the caller asking: its own deadline, not a Tracon ring or a
    /// tool above it.
    /// </summary>
    /// <param name="exception">The cancellation.</param>
    /// <returns><see langword="true"/> when this client marked it on the way out.</returns>
    internal static bool IsProviderCancellation(OperationCanceledException exception)
        => exception.Data.Contains(ProviderCancellationKey);

    private static void MarkProviderCancellation(OperationCanceledException exception)
        => exception.Data[ProviderCancellationKey] = true;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            MarkProviderCancellation(exception);

            throw;
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
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            MarkProviderCancellation(exception);

            throw;
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
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    MarkProviderCancellation(exception);

                    throw;
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

    /// <summary>Gets the HTTP status the provider answered with, or <see langword="null"/> when it carried none.</summary>
    internal int? StatusCode { get; private init; }

    internal static ProviderInvocationException UpstreamFailure(Exception exception, string provider)
    {
        // Read with the reader every retry decision uses, which takes
        // HttpRequestException.StatusCode first. The text reader alone missed it:
        // .NET writes "Response status code does not indicate success: 401
        // (Unauthorized).", so the record lost the status and a 401 looked like a
        // connection that never got an answer.
        var status = ProviderHttpStatus.Read(exception);

        return new(
            ProviderFailureNormalizer.UpstreamErrorType,
            ProviderFailureNormalizer.DescribeUpstreamFailure(provider, ProviderFailureNormalizer.ReadFaultType(exception), status),
            exception)
        {
            StatusCode = status,
        };
    }
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
            when (ForeignProviderInvocationException.Find(exception) is { } foreign)
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
                    when (ForeignProviderInvocationException.Find(exception) is { } foreign)
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
}
