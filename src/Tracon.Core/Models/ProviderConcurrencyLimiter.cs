using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Limits concurrent outgoing calls per model provider.
/// </summary>
/// <remarks>
/// <para>
/// The goal is to avoid producing a burst of <c>429</c> responses; the
/// circuit breaker only reacts once a provider is already failing. A request
/// over the limit <strong>waits</strong> for a slot instead of being rejected
/// — rejecting outright would turn a short traffic spike into exactly the
/// failure this option exists to prevent. The wait is bounded by the
/// caller's own cancellation token, so it can never hang a request forever.
/// </para>
/// <para>
/// <see cref="TraconModelConcurrencyOptions.MaxConcurrentCallsPerProvider"/>
/// is read via <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every
/// call. When it is <see langword="null"/> — the default — <see cref="AcquireAsync"/>
/// returns immediately with no semaphore allocated and no wait: the hot path
/// costs a single null check. Once a provider's first limited call creates its
/// <see cref="SemaphoreSlim"/>, that provider's numeric limit is fixed for the
/// semaphore's lifetime — <see cref="SemaphoreSlim"/> cannot resize; a runtime
/// change to the configured number takes effect only for a provider that has
/// not yet been limited.
/// </para>
/// </remarks>
public sealed class ProviderConcurrencyLimiter
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<TraconOptions> _optionsMonitor;

    /// <summary>Creates a new limiter.</summary>
    /// <param name="optionsMonitor">The runtime settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public ProviderConcurrencyLimiter(IOptionsMonitor<TraconOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
    }

    /// <summary>
    /// Reserves an outgoing call slot for the given provider, waiting if the
    /// limit is currently reached.
    /// </summary>
    /// <param name="providerName">The provider name. The limit is tracked per this name.</param>
    /// <param name="cancellationToken">
    /// The cancellation token. Canceling while waiting releases no slot (none
    /// was reserved) and throws <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// The reserved slot, or <see langword="null"/> when no limit is
    /// configured. Disposing the returned object releases the slot.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> is empty.</exception>
    public async ValueTask<IDisposable?> AcquireAsync(string providerName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var limit = _optionsMonitor.CurrentValue.ModelConcurrency.MaxConcurrentCallsPerProvider;

        if (limit is not { } maxConcurrentCalls)
        {
            return null;
        }

        var semaphore = _semaphores.GetOrAdd(
            providerName,
            static (_, max) => new SemaphoreSlim(max, max),
            Math.Max(1, maxConcurrentCalls));

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}

/// <summary>
/// Reserves a <see cref="ProviderConcurrencyLimiter"/> slot around every real
/// outgoing call.
/// </summary>
/// <remarks>
/// Sits closest to the raw client in <c>ModelProviderRegistry.CreateChatClient</c>
/// — INSIDE the tool-call loop, so a slot is reserved for every turn of a
/// multi-call agent run, not once per agent turn.
/// </remarks>
internal sealed class ProviderConcurrencyLimitingChatClient(
    string providerName,
    IChatClient inner,
    ProviderConcurrencyLimiter limiter) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var lease = await limiter.AcquireAsync(providerName, cancellationToken).ConfigureAwait(false);

        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lease = await limiter.AcquireAsync(providerName, cancellationToken).ConfigureAwait(false);

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }
}
