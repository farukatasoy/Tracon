using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;

namespace Tracon;

/// <summary>
/// Lets the instance that receives the provider's redirect hand the authorization code to the
/// instance that started the flow, through the application's <see cref="IDistributedCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// The flow cannot move: the SDK's <c>McpClient.CreateAsync</c> call that waits for the code, and
/// the PKCE verifier it holds, live in one process. The redirect, on the other hand, goes to
/// whichever instance the load balancer picks. The two meet in the cache:
/// </para>
/// <list type="number">
///   <item><description>The owner leaves a <em>flow marker</em> under the digest of the <c>state</c>
///   before it hands out the authorization address.</description></item>
///   <item><description>The instance that receives the redirect finds no flow of its own, looks for the
///   marker, and leaves the code in a <em>callback</em> entry.</description></item>
///   <item><description>The owner looks for that entry while its flow waits, takes it (it reads it and
///   removes it), and exchanges the code.</description></item>
///   <item><description>The owner leaves an <em>outcome</em>; the instance that received the redirect
///   waits for it and answers the browser.</description></item>
/// </list>
/// <para>
/// <strong>The redirect endpoint takes no bearer token and has no rate limit of its own, and this class
/// writes to the cache for it.</strong>
/// The marker is what bounds that: a value is written only for a <c>state</c> an owner announced, and
/// the <c>state</c> is a value the SDK generated and only the administrator's browser received. A
/// request with any other value reaches no write. Values that are too long or not shaped like a
/// <c>state</c> are refused before the cache is touched, and every entry ends by itself.
/// Code, issuer and outcome are encrypted with <see cref="IContentProtector"/>; a reader of the cache
/// does not get a code, and a code is useless without the verifier that stays in the owner's process.
/// </para>
/// </remarks>
internal sealed class McpOAuthRendezvous
{
    /// <summary>The longest authorization code accepted; the provider's codes are a few hundred characters.</summary>
    internal const int MaxCodeLength = 4096;

    /// <summary>The longest <c>iss</c> value accepted.</summary>
    internal const int MaxIssuerLength = 2048;

    private const int MinStateLength = 16;
    private const int MaxStateLength = 128;

    private static readonly byte[] MarkerValue = [1];

    private readonly McpOAuthDistributedBackend _backend;
    private readonly ILogger _logger;

    /// <summary>Initializes the rendezvous.</summary>
    /// <param name="backend">The distributed services; it must be enabled.</param>
    /// <param name="logger">The logger.</param>
    public McpOAuthRendezvous(McpOAuthDistributedBackend backend, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        if (!backend.Enabled)
        {
            throw new ArgumentException("The distributed OAuth state is off.", nameof(backend));
        }

        _backend = backend;
        _logger = logger;
    }

    /// <summary>Says whether a <c>state</c> has the shape of one the SDK generates.</summary>
    /// <param name="state">The value the provider sent back.</param>
    /// <returns><see langword="true"/> for 16 to 128 URL-safe characters.</returns>
    public static bool IsAcceptableState(string? state)
    {
        if (state is null || state.Length is < MinStateLength or > MaxStateLength)
        {
            return false;
        }

        foreach (var c in state)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Says whether the values of a redirect are small enough to be left in the cache.</summary>
    /// <param name="code">The authorization code.</param>
    /// <param name="iss">The <c>iss</c> value.</param>
    /// <returns><see langword="true"/> when both fit their limits.</returns>
    public static bool AreAcceptableValues(string? code, string? iss)
        => (code is null || code.Length <= MaxCodeLength) && (iss is null || iss.Length <= MaxIssuerLength);

    /// <summary>Leaves the marker that says this instance waits for the flow of <paramref name="state"/>.</summary>
    /// <param name="state">The <c>state</c> the SDK put into the authorization address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the marker is written.</returns>
    public async ValueTask AnnounceAsync(string state, CancellationToken cancellationToken)
        => await _backend.Cache
            .SetAsync(
                McpOAuthCacheKeys.Flow(state),
                MarkerValue,
                Expiring(_backend.Timing.FlowLifetime),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Says whether an instance announced the flow of <paramref name="state"/>.</summary>
    /// <param name="state">The <c>state</c> the provider sent back.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> while the marker is in the cache.</returns>
    public async ValueTask<bool> IsAnnouncedAsync(string state, CancellationToken cancellationToken)
        => await _backend.Cache.GetAsync(McpOAuthCacheKeys.Flow(state), cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Leaves the code for the instance that owns the flow.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <param name="code">The authorization code; <see langword="null"/> when the provider sent none.</param>
    /// <param name="iss">The <c>iss</c> value, when the provider sent one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
    public async ValueTask DepositCallbackAsync(string state, string? code, string? iss, CancellationToken cancellationToken)
        => await _backend.Cache
            .SetAsync(
                McpOAuthCacheKeys.Callback(state),
                McpOAuthEnvelope.Seal(_backend.Protector, new McpCallbackEnvelope(code, iss), McpOAuthJsonContext.Default.McpCallbackEnvelope),
                Expiring(_backend.Timing.FlowLifetime),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Takes the code another instance left: reads it and removes it, so it is used once.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The code; <see langword="null"/> when none is there, or when it cannot be read.</returns>
    /// <remarks>
    /// Only the owner of the flow takes, and the owner is one instance, so the read and the removal need
    /// no atomic step.
    /// </remarks>
    public async ValueTask<McpCallbackEnvelope?> TakeCallbackAsync(string state, CancellationToken cancellationToken)
    {
        var key = McpOAuthCacheKeys.Callback(state);
        var bytes = await _backend.Cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

        if (bytes is null)
        {
            return null;
        }

        await _backend.Cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        if (McpOAuthEnvelope.TryOpen(_backend.Protector, bytes, McpOAuthJsonContext.Default.McpCallbackEnvelope, out var envelope))
        {
            return envelope;
        }

        // Somebody wrote an entry that is not an encrypted callback. It is gone now, and the
        // flow keeps waiting for the real one.
        _logger.LogWarning("A callback entry for an MCP OAuth flow could not be read and was discarded.");

        return null;
    }

    /// <summary>Waits until another instance leaves a code for the flow, and takes it.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <param name="cancellationToken">Ends the wait: the flow finished, or its time is over.</param>
    /// <returns>The code.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<McpCallbackEnvelope> AwaitCallbackAsync(string state, CancellationToken cancellationToken)
    {
        var envelope = await McpOAuthPoll
            .UntilAsync(
                async token =>
                {
                    try
                    {
                        return await TakeCallbackAsync(state, token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (OperationCancellation.IsFailure(ex, token))
                    {
                        // A cache that fails for a moment must not end an authorization the
                        // administrator is in the middle of.
                        _logger.LogWarning(ex, "The distributed cache could not be read while an MCP OAuth flow waited for its callback.");

                        return null;
                    }
                },
                _backend.Timing.CallbackPoll,
                limit: null,
                _backend.Clock,
                cancellationToken)
            .ConfigureAwait(false);

        return envelope!;
    }

    /// <summary>Removes the entries of a flow; it ends by itself anyway.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <returns>A task that completes when the work is done.</returns>
    public async ValueTask ForgetAsync(string state)
    {
        try
        {
            await _backend.Cache.RemoveAsync(McpOAuthCacheKeys.Flow(state), CancellationToken.None).ConfigureAwait(false);
            await _backend.Cache.RemoveAsync(McpOAuthCacheKeys.Callback(state), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The entries of an MCP OAuth flow could not be removed from the distributed cache; they end by themselves.");
        }
    }

    /// <summary>Leaves the result of the token exchange for the instance that received the redirect.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <param name="outcome">The result.</param>
    /// <returns>A task that completes when the entry is written, or when writing failed.</returns>
    public async ValueTask PublishOutcomeAsync(string state, McpOutcomeEnvelope outcome)
    {
        try
        {
            await _backend.Cache
                .SetAsync(
                    McpOAuthCacheKeys.Outcome(state),
                    McpOAuthEnvelope.Seal(_backend.Protector, outcome, McpOAuthJsonContext.Default.McpOutcomeEnvelope),
                    Expiring(_backend.Timing.OutcomeLifetime),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The instance that waits ends its wait with "timed out"; the flow itself is done.
            _logger.LogWarning(ex, "The result of an MCP OAuth flow could not be written to the distributed cache.");
        }
    }

    /// <summary>Waits for the result of the flow another instance owns.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result; <see langword="null"/> when the owner left none in time.</returns>
    public async Task<McpOutcomeEnvelope?> AwaitOutcomeAsync(string state, CancellationToken cancellationToken)
    {
        var key = McpOAuthCacheKeys.Outcome(state);

        return await McpOAuthPoll
            .UntilAsync(
                async token =>
                {
                    try
                    {
                        var bytes = await _backend.Cache.GetAsync(key, token).ConfigureAwait(false);

                        return bytes is not null
                            && McpOAuthEnvelope.TryOpen(_backend.Protector, bytes, McpOAuthJsonContext.Default.McpOutcomeEnvelope, out var outcome)
                                ? outcome
                                : null;
                    }
                    catch (Exception ex) when (OperationCancellation.IsFailure(ex, token))
                    {
                        // The owner may be done: one failed read is not an answer.
                        _logger.LogWarning(ex, "The distributed cache could not be read while an MCP OAuth callback waited for the result.");

                        return null;
                    }
                },
                _backend.Timing.OutcomePoll,
                _backend.Timing.OutcomeWait,
                _backend.Clock,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Builds the result the SDK's authorization handler returns from a callback envelope.</summary>
    /// <param name="state">The <c>state</c> of the flow.</param>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The result; <see langword="null"/> when the provider sent no code.</returns>
    public static AuthorizationResult? ToAuthorizationResult(string state, McpCallbackEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return string.IsNullOrEmpty(envelope.Code)
            ? null
            : new AuthorizationResult { Code = envelope.Code, State = state, Iss = envelope.Iss };
    }

    private static DistributedCacheEntryOptions Expiring(TimeSpan lifetime)
        => new() { AbsoluteExpirationRelativeToNow = lifetime };
}
