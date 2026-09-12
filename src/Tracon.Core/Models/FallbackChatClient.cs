using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

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
/// fallback provider — the conversation state itself is not carried over;
/// switching providers mid-tool-loop would leave a half-finished conversation
/// state that no provider can resume. A tool call that already completed on
/// an earlier link is not repeated, though: <see cref="GetResponseAsync"/>
/// shares one turn-local <see cref="RecordedToolPlayback"/> ledger across
/// every link it tries — whichever link the model asks the same question on
/// again is answered from the ledger instead of running the tool's body a
/// second time. Only the non-streaming path does this; see the remarks on
/// <see cref="GetStreamingResponseAsync"/> for why.
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
    /// A consumer-supplied retry decision, consulted before Tracon's
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
    /// <remarks>
    /// Builds ONE <see cref="RecordedToolPlayback"/> ledger for the whole
    /// turn: a tool call that completes on one link is recorded into it, and
    /// the next link to ask the same question is answered from there instead
    /// of running the tool's body again — the same mechanism
    /// <see cref="RunContinuationJobHandler"/> uses for a crash-interrupted
    /// run, borrowed here for a provider-interrupted one. The ledger is a
    /// local variable, not <see cref="AsyncLocal{T}"/>: it is threaded
    /// through explicitly via <see cref="ChatOptions.Tools"/> at each link,
    /// the same way <see cref="OptionsForLink"/> already threads the link's
    /// own <see cref="ChatOptions.ModelId"/>.
    /// </remarks>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var buffer = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        Exception? firstFailure = null;
        var firstFailureReason = FallbackSkipReason.None;

        // Empty at link 0; RunLive so a call with no match runs for real
        // (recordLiveCalls writes it back) instead of being treated as a
        // replay mismatch.
        var toolLedger = new RecordedToolPlayback([], ToolPlaybackMismatchPolicy.RunLive, recordLiveCalls: true);

        for (var index = 0; index <= _fallbacks.Count; index++)
        {
            var client = index == 0
                ? _primaryClient
                : await ResolveFallbackClientAsync(index - 1, cancellationToken).ConfigureAwait(false);
            var callOptions = index == 0
                ? OptionsForLink(options, modelId: null, toolLedger)
                : OptionsForLink(options, _fallbacks[index - 1].Model, toolLedger);

            // Set by the exception filter below, read by the catch body that
            // follows it. The filter is the only place the failure is
            // classified, so a consumer-supplied IProviderRetryClassifier is
            // still called exactly ONCE per failed link.
            var reason = FallbackSkipReason.None;

            try
            {
                var response = await client.GetResponseAsync(buffer, callOptions, cancellationToken).ConfigureAwait(false);

                if (index > 0)
                {
                    await RecordFallbackUsedAsync(_fallbacks[index - 1], firstFailureReason, cancellationToken).ConfigureAwait(false);
                }

                return response;
            }
            // 🚨 `when (cancellationToken.IsCancellationRequested)`, not a bare
            // catch. Every official provider SDK calls through HttpClient, and
            // HttpClient reports its OWN request timeout as a
            // TaskCanceledException wrapping a TimeoutException - with nobody
            // having cancelled anything. A bare catch here rethrew that as a
            // cancellation, so a provider timeout never reached the next link:
            // the one failure a fallback chain exists for was the one it
            // skipped. Measured in Phase 157. CircuitBreakingChatClient already
            // used this filter; this client did not.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // A non-retryable failure (auth, an unrecognized error, ...) is
            // never masked by trying the next link — whichever link produced
            // it, from K1's "zero surprise": hiding a configuration error
            // behind a provider switch costs more than the switch saves.
            catch (Exception ex) when ((reason = ClassifyFailure(ex)) == FallbackSkipReason.None)
            {
                throw;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;

                // The reason recorded on the event is the FIRST link's, for
                // the same reason ChainExhausted reports the first failure:
                // "why did we leave the primary" is the operator's question,
                // not "why did link three also fail".
                if (firstFailureReason == FallbackSkipReason.None)
                {
                    firstFailureReason = reason;
                }

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
    /// <remarks>
    /// <strong>Carries no tool ledger</strong>: a stream can only fall back
    /// before its first chunk (<c>sawUpdate</c> below), and at that point the
    /// model has not produced any content yet, let alone a tool call — there
    /// is nothing a ledger could ever have recorded. Adding one here would be
    /// pure overhead on every streaming call. <see cref="OptionsForLink"/> is
    /// still used for its <see cref="ChatOptions.ModelId"/> behavior, with no
    /// ledger passed.
    /// </remarks>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        Exception? firstFailure = null;
        var firstFailureReason = FallbackSkipReason.None;

        for (var index = 0; index <= _fallbacks.Count; index++)
        {
            var client = index == 0
                ? _primaryClient
                : await ResolveFallbackClientAsync(index - 1, cancellationToken).ConfigureAwait(false);
            var callOptions = index == 0 ? options : OptionsForLink(options, _fallbacks[index - 1].Model, toolLedger: null);
            var sawUpdate = false;

            // See GetResponseAsync: the filter classifies once, the catch
            // body reads what it stored.
            var reason = FallbackSkipReason.None;

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
                    // Same filter, same reason as GetResponseAsync above: a
                    // provider timeout is not a cancellation.
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    // Already streamed a chunk to the caller, or the failure
                    // is not one this client retries: propagate as-is. See
                    // GetResponseAsync for why a non-retryable failure is
                    // never masked.
                    catch (Exception ex) when (sawUpdate || (reason = ClassifyFailure(ex)) == FallbackSkipReason.None)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        firstFailure ??= ex;

                        if (firstFailureReason == FallbackSkipReason.None)
                        {
                            firstFailureReason = reason;
                        }

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
                            await RecordFallbackUsedAsync(_fallbacks[index - 1], firstFailureReason, cancellationToken).ConfigureAwait(false);
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
    /// <summary>
    /// Decides whether <paramref name="exception"/> makes this link worth
    /// skipping, and names why.
    /// </summary>
    /// <returns>
    /// <see cref="FallbackSkipReason.None"/> when the failure does not fall
    /// back; otherwise the classified reason, which is recorded on the
    /// <see cref="RunEventType.ModelFallbackUsed"/> event.
    /// </returns>
    /// <remarks>
    /// The retry DECISION is unchanged: a cancellation never falls back, a
    /// registered <see cref="IProviderRetryClassifier"/> still wins over the
    /// built-in rules, and a classifier that throws still defers to them.
    /// The built-in rules are consulted for the REASON even when the
    /// consumer's classifier already said <see cref="ProviderRetryDecision.Retry"/>,
    /// so the recorded event names something more specific than "a consumer
    /// rule said so" whenever it can.
    /// </remarks>
    private FallbackSkipReason ClassifyFailure(Exception exception)
    {
        if (FallbackRetryClassifier.IsCancellation(exception))
        {
            return FallbackSkipReason.None;
        }

        if (_retryClassifier is not null)
        {
            try
            {
                var decision = _retryClassifier.Classify(exception);

                if (decision == ProviderRetryDecision.DoNotRetry)
                {
                    return FallbackSkipReason.None;
                }

                if (decision == ProviderRetryDecision.Retry)
                {
                    var classified = FallbackRetryClassifier.Classify(exception);

                    return classified == FallbackSkipReason.None
                        ? FallbackSkipReason.Classifier
                        : classified;
                }
            }
            catch (Exception classifierException)
            {
                _loggerFactory?
                    .CreateLogger("Tracon.ModelProvider")
                    .LogError(
                        classifierException,
                        "The registered IProviderRetryClassifier threw; falling back to the built-in retry rules.");
            }
        }

        return FallbackRetryClassifier.Classify(exception);
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> a link is called with.
    /// </summary>
    /// <param name="options">The caller's original options. Never mutated.</param>
    /// <param name="modelId">
    /// The model name to force, or <see langword="null"/> to leave
    /// <see cref="ChatOptions.ModelId"/> exactly as the caller set it (link 0,
    /// the primary).
    /// </param>
    /// <param name="toolLedger">
    /// The turn's shared tool ledger, or <see langword="null"/> on the
    /// streaming path, which never wraps tools (see
    /// <see cref="GetStreamingResponseAsync"/>).
    /// </param>
    /// <remarks>
    /// <para>
    /// A fallback link's <see cref="ModelFallback.Model"/> can legitimately
    /// differ from the primary's — that is the entire point of naming it
    /// separately. Forwarding the primary's <see cref="ChatOptions.ModelId"/>
    /// unchanged would send the wrong model name to a provider that has no
    /// idea what "the primary's model" means.
    /// </para>
    /// <para>
    /// <strong>The caller's <see cref="ChatOptions"/> and its
    /// <see cref="ChatOptions.Tools"/> list are never mutated in place</strong>:
    /// <see cref="ChatOptions.Clone"/> already returns an independent
    /// <see cref="ChatOptions.Tools"/> list, not a shared reference to the
    /// original, and every other option (instructions, reasoning, ...) is
    /// kept intact by it. Only link 0 with no work to do (no forced model, no
    /// tools to wrap) skips cloning entirely and reuses the caller's instance
    /// as-is. This matters because the caller's options can be a cached,
    /// reused object (<c>CompiledAgentCache</c>): a fallback attempt must
    /// never leave that object permanently wrapped for the agent's next,
    /// unrelated run.
    /// </para>
    /// </remarks>
    private static ChatOptions? OptionsForLink(ChatOptions? options, string? modelId, RecordedToolPlayback? toolLedger)
    {
        var toolsToWrap = toolLedger is not null ? options?.Tools : null;

        if (modelId is null && toolsToWrap is not { Count: > 0 })
        {
            return options;
        }

        var linkOptions = options?.Clone() ?? new ChatOptions();

        if (modelId is not null)
        {
            linkOptions.ModelId = modelId;
        }

        if (toolsToWrap is { Count: > 0 })
        {
            // Only AIFunction carries a body to gate; a client-side tool
            // declaration (AddClientTool) has none and is passed through
            // unchanged.
            linkOptions.Tools = [.. toolsToWrap.Select(tool => tool is AIFunction function ? (AITool)toolLedger!.Wrap(function) : tool)];
        }

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

    private async ValueTask RecordFallbackUsedAsync(
        ModelFallback usedLink,
        FallbackSkipReason reason,
        CancellationToken cancellationToken)
    {
        TraconRunContext.Current?.FallbackAttribution?.Record(usedLink.Provider, usedLink.Model);

        if (TraconRunContext.Current?.Writer is not { } writer)
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
                Reason = reason.ToWireValue(),
            },
            TraconCoreJsonContext.Default.ModelFallbackUsedEventPayload);

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.ModelFallbackUsed)
            {
                Text = $"{usedLink.Provider}/{usedLink.Model}",
                Payload = payload,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private TraconProviderUnavailableException ChainExhausted(Exception firstFailure)
    {
        var triedProviders = new List<string>(_fallbacks.Count + 1) { _primaryBinding.Provider };

        foreach (var fallback in _fallbacks)
        {
            triedProviders.Add(fallback.Provider);
        }

        return new TraconProviderUnavailableException(
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

    /// <summary>
    /// Gets the classified reason the primary link was skipped
    /// (<see cref="FallbackSkipReason"/>'s wire value).
    /// </summary>
    /// <remarks>
    /// Never the provider's own message: a provider message can quote the
    /// request or the response body, and a run event is permanent.
    /// </remarks>
    public required string Reason { get; init; }
}

/// <summary>Why a model link was skipped in favor of the next fallback link.</summary>
/// <remarks>
/// A CLOSED SET. The wire values are written into the
/// <see cref="RunEventType.ModelFallbackUsed"/> event payload and read by
/// operators and consumer dashboards, so an existing value never changes
/// meaning and never disappears; a new cause gets a NEW value.
/// </remarks>
internal enum FallbackSkipReason
{
    /// <summary>The failure does not fall back; the link's error propagates.</summary>
    None = 0,

    /// <summary>The circuit breaker already reported the provider as down.</summary>
    ProviderUnavailable = 1,

    /// <summary>The provider reported a rate limit.</summary>
    RateLimited = 2,

    /// <summary>The provider answered with a retryable HTTP status.</summary>
    HttpError = 3,

    /// <summary>The call never reached the provider (connection, DNS, stream reset).</summary>
    TransportError = 4,

    /// <summary>
    /// A consumer-registered <see cref="IProviderRetryClassifier"/> decided to
    /// retry a failure the built-in rules do not recognize.
    /// </summary>
    Classifier = 5,

    /// <summary>The provider did not answer within its own deadline.</summary>
    /// <remarks>
    /// Distinct from <see cref="TransportError"/>: the call DID reach the
    /// provider, it just never came back. An operator reading the fallback
    /// event needs to tell "the provider is unreachable" from "the provider is
    /// overloaded", because the two have different responses.
    /// </remarks>
    Timeout = 6,
}

/// <summary>Maps <see cref="FallbackSkipReason"/> to its stable wire value.</summary>
internal static class FallbackSkipReasonExtensions
{
    /// <summary>Gets the wire value written into the run event payload.</summary>
    /// <param name="reason">The reason to write.</param>
    /// <returns>The stable, lower-case wire value.</returns>
    public static string ToWireValue(this FallbackSkipReason reason) => reason switch
    {
        FallbackSkipReason.ProviderUnavailable => "provider_unavailable",
        FallbackSkipReason.RateLimited => "rate_limited",
        FallbackSkipReason.HttpError => "http_error",
        FallbackSkipReason.TransportError => "transport_error",
        FallbackSkipReason.Classifier => "classifier",
        FallbackSkipReason.Timeout => "timeout",

        // Unreachable through FallbackChatClient (an event is only written
        // once a link was actually skipped), but a total switch keeps the
        // payload's contract "always a value" true if that ever changes.
        _ => "unknown",
    };
}

/// <summary>
/// Decides whether a model-call failure is worth retrying on the next
/// <see cref="ModelBinding.Fallbacks"/> link.
/// </summary>
/// <remarks>
/// <para>
/// <c>Tracon.Core</c> has no compile-time reference to a provider
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
    /// <summary>
    /// Determines whether <paramref name="exception"/> should try the next
    /// fallback link.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Classify"/> rather than repeating its rules.
    /// The two answers must never disagree: the decision "do we fall back"
    /// and the recorded reason "why did we fall back" come from a single
    /// expression, so a rule added to one cannot go missing from the other.
    /// </remarks>
    public static bool IsRetryable(Exception exception)
        => Classify(exception) != FallbackSkipReason.None;

    /// <summary>
    /// Classifies why <paramref name="exception"/> makes the current link
    /// worth skipping, or <see cref="FallbackSkipReason.None"/> when it does
    /// not.
    /// </summary>
    /// <remarks>
    /// The returned value is written into the
    /// <see cref="RunEventType.ModelFallbackUsed"/> event payload. It is a
    /// CLOSED SET, never the provider's own message: a provider message can
    /// quote the request or the response body, and a run event is permanent
    /// (the same rule <c>ProviderFailureNormalizer</c> applies at the outer
    /// model-call boundary).
    /// </remarks>
    public static FallbackSkipReason Classify(Exception exception)
    {
        if (IsCancellation(exception))
        {
            return FallbackSkipReason.None;
        }

        // Checked before the message patterns below: a timed-out call carries
        // no HTTP status to recognize, and its own exception type is the only
        // signal there is.
        if (IsTimeout(exception))
        {
            return FallbackSkipReason.Timeout;
        }

        foreach (var candidate in Flatten(exception))
        {
            if (candidate is TraconProviderUnavailableException)
            {
                // The circuit breaker reports the provider as already down —
                // exactly the case this chain exists to route around.
                return FallbackSkipReason.ProviderUnavailable;
            }

            // Never retryable even when otherwise recognized as an HTTP
            // error below: hiding a configuration mistake behind a provider
            // switch costs more than the switch saves. Checked across the
            // whole graph, and BEFORE any "retry" match, so an auth failure
            // wrapped alongside an unrelated retryable-looking inner frame
            // still wins.
            if (AuthenticationStatusPattern().IsMatch(candidate.Message))
            {
                return FallbackSkipReason.None;
            }
        }

        foreach (var candidate in Flatten(exception))
        {
            if (RateLimitPattern().IsMatch(candidate.Message))
            {
                return FallbackSkipReason.RateLimited;
            }

            if (RetryableHttpStatusPattern().IsMatch(candidate.Message))
            {
                return FallbackSkipReason.HttpError;
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
                return FallbackSkipReason.TransportError;
            }
        }

        return FallbackSkipReason.None;
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
        // 🚨 A timeout is NOT a cancellation, even though .NET reports it as
        // one. HttpClient raises TaskCanceledException with an inner
        // TimeoutException when ITS OWN deadline elapses - the exception type
        // alone cannot tell that apart from a caller pressing stop, so the
        // graph is asked for a timeout signal first. Without this, a provider
        // timeout was recorded as a cancelled run and returned to the client
        // as an empty success. Measured in Phase 157.
        if (IsTimeout(exception))
        {
            return false;
        }

        foreach (var candidate in Flatten(exception))
        {
            if (candidate is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a timeout is buried anywhere in
    /// <paramref name="exception"/>'s graph.
    /// </summary>
    /// <remarks>
    /// Two signals, because SDKs differ: a <see cref="TimeoutException"/> frame
    /// (what <c>HttpClient</c> attaches) or a message that says so (what an SDK
    /// wrapping its own transport tends to produce). The message pattern is the
    /// same one <c>DefaultRunErrorClassifier</c> uses, so a failure cannot be a
    /// timeout to one and not to the other.
    /// </remarks>
    internal static bool IsTimeout(Exception exception)
    {
        foreach (var candidate in Flatten(exception))
        {
            if (candidate is TimeoutException)
            {
                return true;
            }

            if (TimeoutPattern().IsMatch(candidate.Message))
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

    // 🚨 Kept character-for-character identical to DefaultRunErrorClassifier's
    // TimeoutPattern. The two answer the same question at two layers - "should
    // the next link be tried" and "which error class is recorded" - and a
    // failure that is a timeout to one but not to the other would produce a
    // run recorded as something the fallback chain never treated as a timeout.
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
