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
    /// reaches <c>ILogger</c> at the call site through <see cref="Log"/>, with
    /// the credential of the call replaced.
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
        foreach (var frame in ProviderFailureGraph.Frames(exception))
        {
            if (ReadFrameStatus(frame) is { } status)
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

        while (candidate is AggregateException && ProviderFailureGraph.Children(candidate) is [var first, ..])
        {
            candidate = first;
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

    /// <summary>Writes a provider failure to the log with the call's credential replaced.</summary>
    /// <param name="loggerFactory">The logger factory, or <see langword="null"/> when the host registered none.</param>
    /// <param name="binding">The model binding of the call.</param>
    /// <param name="exception">The raw failure. When the call had a credential it is not handed to the logger as it is.</param>
    /// <param name="operation">What the call was doing.</param>
    /// <param name="mask">The mask of the credential the call was made with.</param>
    /// <remarks>
    /// When the call had a credential the logger gets <see cref="ProviderSecretMask.View"/>, not the exception: a
    /// provider's error text can quote the key the call sent, and a logger writes the exception with every inner
    /// exception. The text an operator needs (type names, status, the rest of the provider's message, the stack) stays.
    /// A call with no credential to hide logs the exception itself, as it always did.
    /// </remarks>
    internal static void Log(
        ILoggerFactory? loggerFactory,
        ModelBinding binding,
        Exception exception,
        string operation,
        ProviderSecretMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        var logger = loggerFactory?.CreateLogger("Tracon.ModelProvider");

        if (logger is null || !logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        logger.LogError(
            mask.Shown(exception),
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
/// <remarks>
/// <para>
/// The original stays whole in <see cref="Original"/>: the retry classifier, the circuit breaker and the status reader
/// decide from it, through <see cref="ProviderFailureGraph"/>. What this exception SAYS is masked. Its message and its
/// <see cref="ToString"/> replace the call's credential, so the chat telemetry ring above it (which copies the message
/// to the span's status and logs the exception) and every logger further up write the provider's text without the key.
/// </para>
/// <para>
/// The inner exception is a <see cref="MaskedFailureView"/> of the original, so a writer that ignores
/// <see cref="Exception.Message"/> and <see cref="ToString"/> and serializes the graph property by property (an error
/// tracker, a structured exception formatter) reaches the type names, messages and stack of every frame with the
/// credential replaced, and no raw frame. A call with no credential value to hide has nothing to replace, and its
/// inner exception is the original: the writer keeps the real types.
/// </para>
/// </remarks>
internal sealed class ForeignProviderInvocationException : Exception
{
    private readonly ProviderSecretMask _mask;

    /// <summary>Marks <paramref name="original"/> as thrown by the provider client.</summary>
    /// <param name="original">The exception the provider client threw.</param>
    /// <param name="mask">The mask of the credential values the client sends.</param>
    internal ForeignProviderInvocationException(Exception original, ProviderSecretMask mask)
        : this(original, mask, ShownInner(original, mask))
    {
    }

    private ForeignProviderInvocationException(Exception original, ProviderSecretMask mask, Exception inner)
        : base(MaskedMessage(original, mask), inner)
    {
        _mask = mask;
        Original = original;
    }

    /// <summary>Gets the exception the provider client threw, whole and unmasked.</summary>
    internal Exception Original { get; }

    /// <summary>Builds the same failure with the provider client's own exception as its inner exception.</summary>
    /// <returns>An exception with the masked message and text, for the code that decides from the graph.</returns>
    internal ForeignProviderInvocationException ForDecision() => new(Original, _mask, Original);

    /// <inheritdoc />
    public override string ToString()
    {
        try
        {
            return _mask.Apply(base.ToString());
        }
        catch (Exception)
        {
            return ProviderSecretMask.Withheld(this);
        }
    }

    private static Exception ShownInner(Exception original, ProviderSecretMask mask)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(mask);

        return mask.Shown(original);
    }

    private static string MaskedMessage(Exception original, ProviderSecretMask mask)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(mask);

        return mask.MessageOf(original);
    }

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
        foreach (var frame in ProviderFailureGraph.Frames(exception))
        {
            if (frame is ForeignProviderInvocationException foreign)
            {
                return foreign;
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
internal sealed class ProviderFailureTaggingChatClient(IChatClient inner, ProviderSecretMask mask) : DelegatingChatClient(inner)
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
            throw new ForeignProviderInvocationException(exception, mask);
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
            throw new ForeignProviderInvocationException(exception, mask);
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
                    throw new ForeignProviderInvocationException(exception, mask);
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
/// <remarks>
/// The message is a fixed sentence plus facts Tracon chose. The provider's own failure stays whole in
/// <see cref="Original"/>, so a retry decision above this ring (a workflow node, a job) reads the same graph the
/// fallback chain did, through <see cref="ProviderFailureGraph"/>. What a writer reaches is masked: <see cref="ToString"/>
/// replaces the call's credential, and the inner exception is a <see cref="MaskedFailureView"/> of the original (the
/// original itself when the call had no credential value to hide). The loggers that take this exception (the run
/// recorder, the endpoints, the protocol adapters) write the provider's text without the key of the call.
/// </remarks>
internal sealed class ProviderInvocationException : TraconException
{
    private readonly ProviderSecretMask _mask;

    private ProviderInvocationException(string errorType, string message, Exception original, Exception inner, ProviderSecretMask mask)
        : base(message, inner)
    {
        ErrorType = errorType;
        _mask = mask;
        Original = original;
    }

    public override string ErrorType { get; }

    /// <summary>Gets the HTTP status the provider answered with, or <see langword="null"/> when it carried none.</summary>
    internal int? StatusCode { get; private init; }

    /// <summary>Gets the exception the provider client threw, whole and unmasked.</summary>
    internal Exception Original { get; }

    /// <summary>Builds the same failure with the provider client's own exception as its inner exception.</summary>
    /// <returns>An exception with the masked text, for the code that decides from the graph.</returns>
    internal ProviderInvocationException ForDecision()
        => new(ErrorType, Message, Original, Original, _mask) { StatusCode = StatusCode };

    /// <inheritdoc />
    public override string ToString()
    {
        try
        {
            return _mask.Apply(base.ToString());
        }
        catch (Exception)
        {
            return ProviderSecretMask.Withheld(this);
        }
    }

    internal static ProviderInvocationException UpstreamFailure(Exception exception, string provider, ProviderSecretMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        // Read with the reader every retry decision uses, which takes
        // HttpRequestException.StatusCode first. The text reader alone missed it:
        // .NET writes "Response status code does not indicate success: 401
        // (Unauthorized).", so the record lost the status and a 401 looked like a
        // connection that never got an answer.
        var status = ProviderHttpStatus.Read(exception);

        return new(
            ProviderFailureNormalizer.UpstreamErrorType,
            ProviderFailureNormalizer.DescribeUpstreamFailure(provider, ProviderFailureNormalizer.ReadFaultType(exception), status),
            exception,
            mask.Shown(exception),
            mask)
        {
            StatusCode = status,
        };
    }
}

/// <summary>Preserves internal retry semantics, then masks the final provider failure.</summary>
internal sealed class ProviderFailureNormalizingChatClient(
    ModelBinding binding,
    IChatClient inner,
    ILoggerFactory? loggerFactory,
    ProviderSecretMask mask)
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
            ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "invocation", mask);
            throw ProviderInvocationException.UpstreamFailure(exception.Original, binding.Provider, mask);
        }
        catch (TraconProviderUnavailableException exception)
            when (ForeignProviderInvocationException.Find(exception) is { } foreign)
        {
            ProviderFailureNormalizer.Log(loggerFactory, binding, foreign.Original, "invocation", mask);
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
            ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "streaming invocation", mask);
            throw ProviderInvocationException.UpstreamFailure(exception.Original, binding.Provider, mask);
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
                    ProviderFailureNormalizer.Log(loggerFactory, binding, exception.Original, "streaming invocation", mask);
                    throw ProviderInvocationException.UpstreamFailure(exception.Original, binding.Provider, mask);
                }
                catch (TraconProviderUnavailableException exception)
                    when (ForeignProviderInvocationException.Find(exception) is { } foreign)
                {
                    ProviderFailureNormalizer.Log(loggerFactory, binding, foreign.Original, "streaming invocation", mask);
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
