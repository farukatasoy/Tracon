using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Bir model saglayicisi ardisik hata verdiginde istekleri gecici olarak kesen devre
/// kesici. Saglayici adina gore durum tutar.
/// </summary>
/// <remarks>
/// <para>
/// Uc durumlu klasik devre kesici deseni: <c>Closed</c> (istekler gecer),
/// <c>Open</c> (istekler aninda <see cref="AgentPrismProviderUnavailableException"/>
/// ile reddedilir, saglayiciya hicbir cagri gitmez) ve <c>HalfOpen</c>
/// (<c>BreakDuration</c> dolunca acilan tek deneme penceresi).
/// </para>
/// <para>
/// Durum bir <see cref="ConcurrentDictionary{TKey,TValue}"/> uzerinde degismez
/// (<see langword="record"/>) anlik goruntulerle, karsilastir-ve-degistir (CAS)
/// donguleriyle tutulur — kilit (lock) kullanilmaz. <c>Wrap</c> ile donen
/// <see cref="IChatClient"/> her cagridan once <see cref="EnsureRequestAllowed"/>,
/// basaridan sonra <see cref="RecordSuccess"/>, hatadan sonra
/// <see cref="RecordFailure"/> cagirir.
/// </para>
/// <para>
/// <see cref="AgentPrismCircuitBreakerOptions.Enabled"/> her cagride
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> uzerinden okunur; calisma
/// aninda kapatilirsa devre kesici o andan itibaren devre disi kalir.
/// </para>
/// </remarks>
public sealed class ModelProviderCircuitBreaker
{
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<AgentPrismOptions> _optionsMonitor;
    private readonly TimeProvider _timeProvider;

    /// <summary>Yeni bir devre kesici olusturur.</summary>
    /// <param name="optionsMonitor">Calisma zamani ayarlari.</param>
    /// <param name="timeProvider">Zaman kaynagi. <see langword="null"/> ise sistem saati kullanilir.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> <see langword="null"/> ise.</exception>
    public ModelProviderCircuitBreaker(IOptionsMonitor<AgentPrismOptions> optionsMonitor, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Devre kesici su an acik mi.</summary>
    public bool IsEnabled => _optionsMonitor.CurrentValue.CircuitBreaker.Enabled;

    /// <summary>Verilen sohbet istemcisini bu saglayici icin devre kesiciyle sarar.</summary>
    /// <param name="providerName">Saglayici adi. Devre durumu bu ada gore tutulur.</param>
    /// <param name="inner">Sarmalanacak istemci.</param>
    /// <returns>Devre kesici ile sarilmis istemci.</returns>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> bos ise.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> <see langword="null"/> ise.</exception>
    public IChatClient Wrap(string providerName, IChatClient inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(inner);

        return new CircuitBreakingChatClient(providerName, inner, this);
    }

    /// <summary>
    /// Devre <c>Open</c> durumdaysa ve mola suresi dolmadiysa
    /// <see cref="AgentPrismProviderUnavailableException"/> atar. Mola suresi
    /// dolduysa devreyi <c>HalfOpen</c>'a gecirir ve cagirana tek denemeyi birakir.
    /// </summary>
    /// <param name="providerName">Saglayici adi.</param>
    /// <exception cref="AgentPrismProviderUnavailableException">Devre acik ise.</exception>
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
                    $"'{providerName}' saglayicisi devre kesici tarafindan gecici olarak durduruldu " +
                    $"({current.ConsecutiveFailures} ardisik hata). " +
                    $"{(breakDuration - elapsed).TotalSeconds:F0} sn sonra yeniden denenecek.")
                {
                    ProviderName = providerName,
                    RetryAfter = current.OpenedAt + breakDuration,
                };
            }

            // Mola suresi doldu: tek bir denemeye izin ver. Baska bir istek ayni anda
            // buraya gelirse CAS basarisiz olur ve dongu yeniden okur; ikinci istek
            // artik HalfOpen gorur ve normal Closed-gibi davranista gecer (bilerek —
            // tek deneme garantisi kesin degil, ama devrenin surekli acik kalmasindan
            // iyidir).
            if (_states.TryUpdate(providerName, current with { Phase = CircuitPhase.HalfOpen }, current))
            {
                return;
            }
        }
    }

    /// <summary>Basarili bir cagridan sonra ardisik hata sayacini sifirlar.</summary>
    /// <param name="providerName">Saglayici adi.</param>
    public void RecordSuccess(string providerName)
    {
        if (!IsEnabled)
        {
            return;
        }

        _states[providerName] = CircuitBreakerState.Initial;
    }

    /// <summary>
    /// Basarisiz bir cagridan sonra ardisik hata sayacini artirir; esik asilirsa
    /// (veya yari-acik denemesi basarisiz olursa) devreyi acar.
    /// </summary>
    /// <param name="providerName">Saglayici adi.</param>
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

            // Yari-acik durumdaki tek deneme basarisiz oldu: hemen yeniden ac.
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
    /// Devre su an acik mi (mola suresi dolmamis <c>Open</c>). Saglik ucunun devre
    /// durumunu raporlamasi icindir; <see cref="EnsureRequestAllowed"/>'in aksine
    /// durumu <strong>degistirmez</strong>.
    /// </summary>
    /// <param name="providerName">Saglayici adi.</param>
    /// <param name="retryAfter">Acik ise kalan mola suresi.</param>
    /// <returns>Devre acik ve mola suresi surmekteyse <see langword="true"/>.</returns>
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
