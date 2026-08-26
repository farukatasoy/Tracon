using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Tries a <see cref="ModelBinding.Fallbacks"/> chain, in order, when the
/// primary provider fails.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Sits outside the circuit breaker, inside the content-filter
/// detector</strong> (measured — placement decides what the rule can see):
/// <c>ModelProviderRegistry.CreateChatClient</c> wraps this client with
/// <c>ContentFilterDetectingChatClient</c>, and this client wraps the
/// already-fully-assembled primary pipeline (content guard, tool loop,
/// telemetry, attachment resolution, circuit breaker). Falling back requires
/// first seeing that the primary's circuit is open; sitting inside the
/// content-filter detector means a provider-filtered response — which is
/// <em>not</em> an exception at this layer, only a <see cref="ChatResponse"/>
/// with <see cref="ChatFinishReason.ContentFilter"/> — passes straight
/// through untouched, and the outer detector makes the one filtering decision
/// for whichever link actually answered. This client never special-cases
/// content filtering because it never sees it as a failure.
/// </para>
/// <para>
/// <strong>Because the loop is behind this client, not in front of it, a
/// fallback restarts the agent's tool-call turn from scratch</strong> on the
/// fallback provider. This is deliberate: switching providers mid-tool-loop
/// would leave a half-finished conversation state that no provider can resume.
/// </para>
/// <para>
/// A fallback link carries only <see cref="ModelFallback.Provider"/> and
/// <see cref="ModelFallback.Model"/> (decision): the fallback
/// binding built for it uses every other <see cref="ModelBinding"/> field at
/// its default, not the primary's values.
/// </para>
/// </remarks>
internal sealed class FallbackChatClient : DelegatingChatClient
{
    private readonly ModelBinding _primaryBinding;
    private readonly IChatClient _primaryClient;
    private readonly IReadOnlyList<ModelFallback> _fallbacks;
    private readonly Func<ModelBinding, CancellationToken, ValueTask<IChatClient>> _buildClient;
    private readonly IProviderRetryClassifier? _retryClassifier;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Dictionary<int, IChatClient> _fallbackClients = [];

    /// <summary>Creates a new fallback client.</summary>
    /// <param name="primaryBinding">The primary binding, whose <see cref="ModelBinding.Fallbacks"/> is the chain.</param>
    /// <param name="primaryClient">The already-assembled primary pipeline.</param>
    /// <param name="buildClient">
    /// Builds the full pipeline for a fallback binding. Passed in rather than
    /// called eagerly: a fallback client is built only once per link that is
    /// actually reached, not once per configured link.
    /// </param>
    /// <param name="retryClassifier">
    /// A consumer-supplied retry decision, consulted before AgentPrism's
    /// built-in rules (<see cref="ProviderRetryDecision.Unknown"/> defers to
    /// them). If <see langword="null"/>, the built-in rules alone decide —
    /// today's exact behavior.
    /// </param>
    /// <param name="loggerFactory">
    /// Used only to log when <paramref name="retryClassifier"/> throws; the
    /// call still falls back to the built-in rules (an observability
    /// function must not break the model-call path).
    /// </param>
    /// <remarks>
    /// <para>
    /// <c>DelegatingChatClient</c> keeps its wrapped client in a PRIVATE
    /// field and exposes no protected accessor for it (measured via
    /// <c>maf-api-kesfi</c>) — <paramref name="primaryClient"/> is therefore
    /// also kept in <see cref="_primaryClient"/> and called directly; the base
    /// class is used only for its <see cref="IDisposable"/>/<c>GetService</c>
    /// passthrough, never for <c>base.GetResponseAsync</c>.
    /// </para>
    /// <para>
    /// <paramref name="buildClient"/> is ASYNC (independent audit
    /// finding). It MUST be <c>ModelProviderRegistry.CreateChatClientAsync</c>,
    /// never the sync <c>CreateChatClient</c>: a fallback link is itself a
    /// <see cref="ModelBinding"/> with its own <see cref="ModelBinding.Provider"/>,
    /// and it must go through the SAME tenant credential/egress resolution as
    /// the primary — otherwise a fallback would silently use the global
    /// credential and bypass the tenant's egress policy entirely.
    /// </para>
    /// </remarks>
    public FallbackChatClient(
        ModelBinding primaryBinding,
        IChatClient primaryClient,
        Func<ModelBinding, CancellationToken, ValueTask<IChatClient>> buildClient,
        IProviderRetryClassifier? retryClassifier = null,
        ILoggerFactory? loggerFactory = null)
        : base(primaryClient)
    {
        _primaryBinding = primaryBinding;
        _primaryClient = primaryClient;
        _fallbacks = primaryBinding.Fallbacks;
        _buildClient = buildClient;
        _retryClassifier = retryClassifier;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var buffer = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        Exception? firstFailure = null;

        for (var index = 0; index <= _fallbacks.Count; index++)
        {
            var client = index == 0
                ? _primaryClient
                : await ResolveFallbackClientAsync(index - 1, cancellationToken).ConfigureAwait(false);
            var callOptions = index == 0 ? options : OptionsForLink(options, _fallbacks[index - 1].Model);

            try
            {
                var response = await client.GetResponseAsync(buffer, callOptions, cancellationToken).ConfigureAwait(false);

                if (index > 0)
                {
                    await RecordFallbackUsedAsync(_fallbacks[index - 1], cancellationToken).ConfigureAwait(false);
                }

                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // A non-retryable failure (auth, an unrecognized error, ...) is
            // never masked by trying the next link — whichever link produced
            // it, from K1's "zero surprise": hiding a configuration error
            // behind a provider switch costs more than the switch saves.
            catch (Exception ex) when (!IsRetryable(ex))
            {
                throw;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;

                // No link is left to try: report the FIRST failure, not this
                // one — the root cause across a chain of transient failures
                // is more useful than whichever link happened to fail last.
                if (index == _fallbacks.Count)
                {
                    throw ChainExhausted(firstFailure);
                }
            }
        }

        throw ChainExhausted(firstFailure!);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        Exception? firstFailure = null;

        for (var index = 0; index <= _fallbacks.Count; index++)
        {
            var client = index == 0
                ? _primaryClient
                : await ResolveFallbackClientAsync(index - 1, cancellationToken).ConfigureAwait(false);
            var callOptions = index == 0 ? options : OptionsForLink(options, _fallbacks[index - 1].Model);
            var sawUpdate = false;

            // 🚨 A stream cannot be retried past its first frame: once a chunk
            // reached the caller, falling back would either duplicate it or
            // corrupt the sequence. IAsyncEnumerator.MoveNextAsync() is driven
            // by hand (instead of `await foreach`) specifically so a failure on
            // the FIRST call — before any `yield return` — can still select the
            // next link, while a failure after that always propagates as-is.
            var enumerator = client.GetStreamingResponseAsync(buffer, callOptions, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            try
            {
                while (true)
                {
                    ChatResponseUpdate update;

                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            break;
                        }

                        update = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    // Already streamed a chunk to the caller, or the failure
                    // is not one this client retries: propagate as-is. See
                    // GetResponseAsync for why a non-retryable failure is
                    // never masked.
                    catch (Exception ex) when (sawUpdate || !IsRetryable(ex))
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        firstFailure ??= ex;

                        if (index == _fallbacks.Count)
                        {
                            throw ChainExhausted(firstFailure);
                        }

                        break;
                    }

                    if (!sawUpdate)
                    {
                        sawUpdate = true;

                        if (index > 0)
                        {
                            await RecordFallbackUsedAsync(_fallbacks[index - 1], cancellationToken).ConfigureAwait(false);
                        }
                    }

                    yield return update;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            if (sawUpdate)
            {
                yield break;
            }
        }

        throw ChainExhausted(firstFailure!);
    }

    /// <summary>
    /// Determines whether <paramref name="exception"/> should try the next
    /// fallback link, consulting <see cref="_retryClassifier"/> first when one
    /// is registered.
    /// </summary>
    /// <remarks>
    /// A buried cancellation is checked BEFORE the seam and short-circuits
    /// unconditionally: a consumer's classifier is never even asked about it —
    /// the seam cannot be used to leak a cancellation into a retry. If the
    /// classifier itself throws, the failure is logged and this falls back to
    /// the built-in rules — an extension point must not
    /// break the model-call path it decorates.
    /// </remarks>
    private bool IsRetryable(Exception exception)
    {
        if (FallbackRetryClassifier.IsCancellation(exception))
        {
            return false;
        }

        if (_retryClassifier is not null)
        {
            try
            {
                var decision = _retryClassifier.Classify(exception);

                if (decision != ProviderRetryDecision.Unknown)
                {
                    return decision == ProviderRetryDecision.Retry;
                }
            }
            catch (Exception classifierException)
            {
                _loggerFactory?
                    .CreateLogger("AgentPrism.ModelProvider")
                    .LogError(
                        classifierException,
                        "The registered IProviderRetryClassifier threw; falling back to the built-in retry rules.");
            }
        }

        return FallbackRetryClassifier.IsRetryable(exception);
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> a fallback link is called with.
    /// </summary>
    /// <remarks>
    /// A fallback link's <see cref="ModelFallback.Model"/> can legitimately
    /// differ from the primary's — that is the entire point of naming it
    /// separately. Forwarding the primary's <see cref="ChatOptions.ModelId"/>
    /// unchanged would send the wrong model name to a provider that has no
    /// idea what "the primary's model" means. <see cref="ChatOptions.Clone"/>
    /// keeps every other option (tools, instructions, reasoning, ...) intact;
    /// the original instance is never mutated, so the primary's own next call
    /// (if this client instance is reused) is unaffected.
    /// </remarks>
    private static ChatOptions? OptionsForLink(ChatOptions? options, string modelId)
    {
        var linkOptions = options?.Clone() ?? new ChatOptions();
        linkOptions.ModelId = modelId;
        return linkOptions;
    }

    private async ValueTask<IChatClient> ResolveFallbackClientAsync(int fallbackIndex, CancellationToken cancellationToken)
    {
        if (_fallbackClients.TryGetValue(fallbackIndex, out var existing))
        {
            return existing;
        }

        var link = _fallbacks[fallbackIndex];

        var binding = new ModelBinding
        {
            Provider = link.Provider,
            Model = link.Model,
        };

        var client = await _buildClient(binding, cancellationToken).ConfigureAwait(false);
        _fallbackClients[fallbackIndex] = client;
        return client;
    }

    private async ValueTask RecordFallbackUsedAsync(ModelFallback usedLink, CancellationToken cancellationToken)
    {
        AgentPrismRunContext.Current?.FallbackAttribution?.Record(usedLink.Provider, usedLink.Model);

        if (AgentPrismRunContext.Current?.Writer is not { } writer)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new ModelFallbackUsedEventPayload
            {
                PrimaryProvider = _primaryBinding.Provider,
                PrimaryModel = _primaryBinding.Model,
                FallbackProvider = usedLink.Provider,
                FallbackModel = usedLink.Model,
            },
            AgentPrismCoreJsonContext.Default.ModelFallbackUsedEventPayload);

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.ModelFallbackUsed)
            {
                Text = $"{usedLink.Provider}/{usedLink.Model}",
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private AgentPrismProviderUnavailableException ChainExhausted(Exception firstFailure)
    {
        var triedProviders = new List<string>(_fallbacks.Count + 1) { _primaryBinding.Provider };

        foreach (var fallback in _fallbacks)
        {
            triedProviders.Add(fallback.Provider);
        }

        return new AgentPrismProviderUnavailableException(
            $"All providers in the fallback chain failed (tried: {string.Join(", ", triedProviders)}).",
            firstFailure)
        {
            ProviderName = _primaryBinding.Provider,
        };
    }
}

/// <summary>The <see cref="RunEventType.ModelFallbackUsed"/> event payload.</summary>
internal sealed record ModelFallbackUsedEventPayload
{
    /// <summary>Gets the primary provider that was skipped.</summary>
    public required string PrimaryProvider { get; init; }

    /// <summary>Gets the primary model that was skipped.</summary>
    public required string PrimaryModel { get; init; }

    /// <summary>Gets the fallback provider that answered instead.</summary>
    public required string FallbackProvider { get; init; }

    /// <summary>Gets the fallback model that answered instead.</summary>
    public required string FallbackModel { get; init; }
}

/// <summary>
/// Decides whether a model-call failure is worth retrying on the next
/// <see cref="ModelBinding.Fallbacks"/> link.
/// </summary>
/// <remarks>
/// <para>
/// <c>AgentPrism.Core</c> has no compile-time reference to a provider
/// SDK's exception types (<c>System.ClientModel.ClientResultException</c>,
/// <c>Azure.RequestFailedException</c>, ...) — only the individual provider
/// packages do. This mirrors <see cref="DefaultRunErrorClassifier"/>'s
/// approach: classification runs on the exception's type NAME and MESSAGE as
/// text, not on a typed catch.
/// </para>
/// <para>
/// <strong>A bare connection failure never reaches the outer catch as
/// itself</strong> — measured against the real OpenAI 2.12.0 client (audit
/// audit): pointing it at a port nothing listens on throws
/// <see cref="AggregateException"/> ("Retry failed after 4 tries...") whose
/// <see cref="Exception.InnerException"/> is
/// <c>System.ClientModel.ClientResultException</c> ("Connection refused
/// (...)") — a message with NO "HTTP nnn" text, because no response was ever
/// received to report a status for. <see cref="IsRetryable"/> therefore
/// inspects the WHOLE exception graph (<see cref="Flatten"/>: self,
/// <see cref="Exception.InnerException"/> recursively, and every branch of an
/// <see cref="AggregateException"/>), not just the outermost exception — the
/// same lesson (real SDKs don't throw the type you'd guess) applied one
/// layer deeper.
/// </para>
/// <para>
/// The list is a closed, positive set: an unrecognized failure does
/// <strong>not</strong> retry by default. A silent provider switch on an
/// error nobody anticipated is a worse outcome than surfacing the error.
/// </para>
/// </remarks>
internal static partial class FallbackRetryClassifier
{
    /// <summary>Determines whether <paramref name="exception"/> should try the next fallback link.</summary>
    public static bool IsRetryable(Exception exception)
    {
        if (IsCancellation(exception))
        {
            return false;
        }

        foreach (var candidate in Flatten(exception))
        {
            if (candidate is AgentPrismProviderUnavailableException)
            {
                // The circuit breaker reports the provider as already down —
                // exactly the case this chain exists to route around.
                return true;
            }

            // Never retryable even when otherwise recognized as an HTTP
            // error below: hiding a configuration mistake behind a provider
            // switch costs more than the switch saves. Checked across the
            // whole graph, and BEFORE any "retry" match, so an auth failure
            // wrapped alongside an unrelated retryable-looking inner frame
            // still wins.
            if (AuthenticationStatusPattern().IsMatch(candidate.Message))
            {
                return false;
            }
        }

        foreach (var candidate in Flatten(exception))
        {
            if (RateLimitPattern().IsMatch(candidate.Message) || RetryableHttpStatusPattern().IsMatch(candidate.Message))
            {
                return true;
            }
        }

        // No frame in the graph carries a recognizable HTTP status: the call
        // never got far enough to receive one (connection refused, DNS,
        // stream reset, ...). At this point a known transport/SDK-client
        // type is itself the signal — a status-less ClientResultException/
        // RequestFailedException is, by construction, a connection failure,
        // not an unrecognized 4xx (those carry "HTTP nnn" and were already
        // handled above).
        foreach (var candidate in Flatten(exception))
        {
            if (TransportExceptionTypePattern().IsMatch(candidate.GetType().FullName ?? string.Empty))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a cancellation is buried anywhere in
    /// <paramref name="exception"/>'s graph (the SDK's own retry pipeline can
    /// wrap one inside an <see cref="AggregateException"/>).
    /// </summary>
    /// <remarks>
    /// Checked BEFORE <see cref="IProviderRetryClassifier"/> is ever
    /// consulted (<see cref="FallbackChatClient"/>) — a cancellation must
    /// never be turned into a retry, and a consumer's classifier cannot
    /// override that.
    /// </remarks>
    internal static bool IsCancellation(Exception exception)
    {
        foreach (var candidate in Flatten(exception))
        {
            if (candidate is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Yields <paramref name="exception"/> and every exception reachable through it.</summary>
    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var nested in Flatten(inner))
                {
                    yield return nested;
                }
            }
        }
        else if (exception.InnerException is { } innerException)
        {
            foreach (var nested in Flatten(innerException))
            {
                yield return nested;
            }
        }
    }

    [GeneratedRegex(@"\bHTTP\s+40[13]\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AuthenticationStatusPattern();

    [GeneratedRegex(
        @"\b429\b|toomanyrequests|\brate[\s_-]?limit(?:ed|ing)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex RateLimitPattern();

    [GeneratedRegex(@"\bHTTP\s+(?:5\d{2}|429)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RetryableHttpStatusPattern();

    // 🚨 Measured (phase 62 audit): includes the SDK WRAPPER types too
    // (clientresultexception/requestfailedexception/apiexception), not just
    // the bare .NET ones — this is safe ONLY because this pattern is checked
    // LAST, after every status-bearing message (including 401/403) already
    // had a chance to match above. A wrapper exception with no status text
    // is, by construction, a connection-level failure.
    [GeneratedRegex(
        @"httprequestexception|socketexception|ioexception|clientresultexception|requestfailedexception|apiexception|aggregateexception",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TransportExceptionTypePattern();
}
