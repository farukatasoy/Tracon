using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Bir arka plan hizmetinin donugusunu kume genelinde tek bir ornege indirger.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> kendi kira/yenileme donugusunu
/// <see cref="SingletonExecutionOptions.LeaseDuration"/>'in UCTE BIRI
/// araliginda calistirir; bu, cagiran hizmetin kendi is araliginin
/// (ornegin MCP kesfinin 5 dakikalik tazeleme araligi) kira suresinden cok
/// daha uzun olabilecegi durumlarda kirayi canli tutar. Cagiran hizmet her
/// kendi turunda yalnizca <see cref="IsHeld"/>'i okur — bu senkron ve
/// ucretsizdir, ek bir veritabani gidisi yaratmaz.
/// </para>
/// <para>
/// <see cref="SingletonExecutionOptions.Enabled"/> <see langword="false"/>
/// (varsayilan) iken <see cref="RunAsync"/> depoya HICBIR sorgu atmadan
/// hemen doner ve <see cref="IsHeld"/> daima <see langword="true"/> kalir —
/// bugunku tek ornekli davranis birebir korunur (K1).
/// </para>
/// <para>
/// 🚨 Bu tip <c>internal</c> DEGIL, <c>public</c>'tir: <c>AgentPrism.Mcp</c>
/// gibi ayri bir derlemeden kullanilmasi gerekir ve <c>InternalsVisibleTo</c>
/// yalniz kendi test projelerini kapsar, kardes paketleri degil. Plandaki
/// "internal yardimci" onerisi bu yuzden uygulanamadi.
/// </para>
/// </remarks>
public sealed class SingletonGuard
{
    private readonly ISingletonLeaseStore _store;
    private readonly IOptionsMonitor<SingletonExecutionOptions> _optionsMonitor;
    private readonly string _leaseName;
    private readonly string _ownerId;
    private readonly ILogger? _logger;

    private volatile bool _holding;

    /// <summary>Yeni bir tek yurutucu bekcisi olusturur.</summary>
    /// <param name="store">Kira deposu.</param>
    /// <param name="optionsMonitor">Tek yurutucu secimi ayarlari.</param>
    /// <param name="leaseName">Korunan isin kume genelinde benzersiz adi.</param>
    /// <param name="logger">Kira kaybi ve hata loglarinin yazildigi gunlukleyici.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> veya <paramref name="optionsMonitor"/> <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="leaseName"/> bos ise.</exception>
    public SingletonGuard(
        ISingletonLeaseStore store,
        IOptionsMonitor<SingletonExecutionOptions> optionsMonitor,
        string leaseName,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);

        _store = store;
        _optionsMonitor = optionsMonitor;
        _leaseName = leaseName;
        _logger = logger;

        _ownerId = optionsMonitor.CurrentValue.OwnerId is { Length: > 0 } configured
            ? configured
            : $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Bu ornegin su an kirayi tuttugu. Tek yurutucu secimi kapaliysa daima
    /// <see langword="true"/>. Senkrondur; cagiran hizmet bunu her turunda
    /// ucretsizce okuyabilir.
    /// </summary>
    public bool IsHeld => !_optionsMonitor.CurrentValue.Enabled || _holding;

    /// <summary>
    /// Kira/yenileme donugusunu baslatir. Tek yurutucu secimi kapaliysa
    /// depoya HICBIR sorgu atmadan hemen doner. <paramref name="stoppingToken"/>
    /// iptal edilince kira (tutuluyorsa) birakilir ve gorev tamamlanir.
    /// </summary>
    /// <param name="stoppingToken">Ev sahibi hizmetin durdurma belirteci.</param>
    /// <returns>Donugu temsil eden gorev.</returns>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!_optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        var leaseDuration = _optionsMonitor.CurrentValue.LeaseDuration;
        var renewInterval = TimeSpan.FromTicks(Math.Max(leaseDuration.Ticks / 3, TimeSpan.FromSeconds(1).Ticks));

        using var timer = new PeriodicTimer(renewInterval);

        try
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal kapanma.
        }
        finally
        {
            if (_holding)
            {
                try
                {
                    await _store.ReleaseAsync(_leaseName, _ownerId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // En iyi cabayla birakma; kira zaten suresi dolunca kendiliginden duser.
                    LogFailure(exception);
                }

                _holding = false;
            }
        }
    }

    /// <summary>Tek bir kiralama/yenileme denemesi yapar.</summary>
    internal async ValueTask TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;

            if (_holding)
            {
                var renewed = await _store
                    .RenewAsync(_leaseName, _ownerId, options.LeaseDuration, cancellationToken)
                    .ConfigureAwait(false);

                if (!renewed)
                {
                    _holding = false;
                    LogLost();
                }
            }
            else
            {
                _holding = await _store
                    .TryAcquireAsync(_leaseName, _ownerId, options.LeaseDuration, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailure(exception);
        }
    }

    private void LogLost()
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("Tek yurutucu kirasi kaybedildi: {LeaseName}.", _leaseName);
        }
    }

    private void LogFailure(Exception exception)
    {
        if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(exception, "Tek yurutucu kira denemesi basarisiz oldu: {LeaseName}.", _leaseName);
        }
    }
}
