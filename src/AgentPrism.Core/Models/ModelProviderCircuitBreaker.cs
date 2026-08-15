using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Circuit breaker that temporarily cuts off requests when a model provider
/// fails consecutively. Holds state per provider name.
/// </summary>
/// <remarks>
/// <para>
/// The classic three-state circuit breaker pattern: <c>Closed</c> (requests
/// pass through), <c>Open</c> (requests are rejected immediately with
/// <see cref="AgentPrismProviderUnavailableException"/>, no call reaches the
/// provider), and <c>HalfOpen</c> (a single trial window opened once
/// <c>BreakDuration</c> has elapsed).
/// </para>
/// <para>
/// State is held on a <see cref="ConcurrentDictionary{TKey,TValue}"/> using
/// immutable (<see langword="record"/>) snapshots and compare-and-swap (CAS)
/// loops — no lock is used. The <see cref="IChatClient"/> returned by
/// <c>Wrap</c> calls <see cref="EnsureRequestAllowed"/> before every call,
/// <see cref="RecordSuccess"/> after a success, and <see cref="RecordFailure"/>
/// after a failure.
/// </para>
/// <para>
/// <see cref="AgentPrismCircuitBreakerOptions.Enabled"/> is read via
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> on every call; if it is
/// turned off at runtime, the circuit breaker becomes inactive from that
/// moment on.
/// </para>
/// </remarks>
public sealed class ModelProviderCircuitBreaker
{
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<AgentPrismOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new circuit breaker.</summary>
    /// <param name="optionsMonitor">The runtime settings.</param>
    /// <param name="timeProvider">The time source. If <see langword="null"/>, the system clock is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public ModelProviderCircuitBreaker(IOptionsMonitor<AgentPrismOptions> optionsMonitor, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Whether the circuit breaker is currently enabled.</summary>
    public bool IsEnabled => _optionsMonitor.CurrentValue.CircuitBreaker.Enabled;

    /// <summary>Wraps the given chat client with the circuit breaker for this provider.</summary>
    /// <param name="providerName">The provider name. Circuit state is held keyed by this name.</param>
    /// <param name="inner">The client to wrap.</param>
    /// <returns>The client wrapped with the circuit breaker.</returns>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public IChatClient Wrap(string providerName, IChatClient inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(inner);

        return new CircuitBreakingChatClient(providerName, inner, this);
    }

    /// <summary>
    /// Throws <see cref="AgentPrismProviderUnavailableException"/> if the circuit
    /// is <c>Open</c> and the break duration has not elapsed. If the break
    /// duration has elapsed, moves the circuit to <c>HalfOpen</c> and lets the
    /// caller have the single trial.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <exception cref="AgentPrismProviderUnavailableException">The circuit is open.</exception>
    public void EnsureRequestAllowed(string providerName)
    {
        if (!IsEnabled)
        {
            return;
        }

        var breakDuration = _optionsMonitor.CurrentValue.CircuitBreaker.BreakDuration;

        while (true)
        {
            var current = _states.GetOrAdd(providerName, static _ => CircuitBreakerState.Initial);

            if (current.Phase != CircuitPhase.Open)
            {
                return;
            }

            var elapsed = _timeProvider.GetUtcNow() - current.OpenedAt;

            if (elapsed < breakDuration)
            {
                throw new AgentPrismProviderUnavailableException(
                    $"The '{providerName}' provider was temporarily stopped by the circuit breaker " +
                    $"({current.ConsecutiveFailures} consecutive failures). " +
                    $"Will retry in {(breakDuration - elapsed).TotalSeconds:F0}s.")
                {
                    ProviderName = providerName,
                    RetryAfter = current.OpenedAt + breakDuration,
                };
            }

            // The break duration has elapsed: allow a single trial. If another
            // request arrives here at the same time, the CAS fails and the loop
            // reads again; the second request then sees HalfOpen and passes
            // through with Closed-like behavior (deliberately — the single-trial
            // guarantee is not exact, but it is better than the circuit staying
            // open forever).
            if (_states.TryUpdate(providerName, current with { Phase = CircuitPhase.HalfOpen }, current))
            {
                return;
            }
        }
    }

    /// <summary>Resets the consecutive-failure counter after a successful call.</summary>
    /// <param name="providerName">The provider name.</param>
    public void RecordSuccess(string providerName)
    {
        if (!IsEnabled)
        {
            return;
        }

        _states[providerName] = CircuitBreakerState.Initial;
    }

    /// <summary>
    /// Increments the consecutive-failure counter after a failed call; opens the
    /// circuit if the threshold is exceeded (or if the half-open trial fails).
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    public void RecordFailure(string providerName)
    {
        if (!IsEnabled)
        {
            return;
        }

        var threshold = _optionsMonitor.CurrentValue.CircuitBreaker.FailureThreshold;

        while (true)
        {
            var current = _states.GetOrAdd(providerName, static _ => CircuitBreakerState.Initial);

            // The single trial in the half-open state failed: reopen immediately.
            if (current.Phase == CircuitPhase.HalfOpen)
            {
                var reopened = new CircuitBreakerState(
                    CircuitPhase.Open,
                    current.ConsecutiveFailures + 1,
                    _timeProvider.GetUtcNow());

                if (_states.TryUpdate(providerName, reopened, current))
                {
                    return;
                }

                continue;
            }

            var failures = current.ConsecutiveFailures + 1;

            var next = failures >= threshold
                ? new CircuitBreakerState(CircuitPhase.Open, failures, _timeProvider.GetUtcNow())
                : new CircuitBreakerState(CircuitPhase.Closed, failures, default);

            if (_states.TryUpdate(providerName, next, current))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Whether the circuit is currently open (<c>Open</c> with the break
    /// duration not yet elapsed). Intended for the health endpoint to report
    /// circuit state; unlike <see cref="EnsureRequestAllowed"/> it
    /// <strong>does not change</strong> the state.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="retryAfter">The remaining break duration, if open.</param>
    /// <returns><see langword="true"/> if the circuit is open and the break duration is still in effect.</returns>
    public bool IsOpen(string providerName, out TimeSpan? retryAfter)
    {
        retryAfter = null;

        if (!IsEnabled || !_states.TryGetValue(providerName, out var state) || state.Phase != CircuitPhase.Open)
        {
            return false;
        }

        var breakDuration = _optionsMonitor.CurrentValue.CircuitBreaker.BreakDuration;
        var elapsed = _timeProvider.GetUtcNow() - state.OpenedAt;

        if (elapsed >= breakDuration)
        {
            return false;
        }

        retryAfter = breakDuration - elapsed;
        return true;
    }

    private enum CircuitPhase
    {
        Closed,
        Open,
        HalfOpen,
    }

    private sealed record CircuitBreakerState(CircuitPhase Phase, int ConsecutiveFailures, DateTimeOffset OpenedAt)
    {
        public static readonly CircuitBreakerState Initial = new(CircuitPhase.Closed, 0, default);
    }
}
