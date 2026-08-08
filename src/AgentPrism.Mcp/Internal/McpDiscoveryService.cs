using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Uzak MCP sunucularinin tool listesini arka planda tazeleyen servis.
/// </summary>
/// <remarks>
/// <para>
/// Ilk kesif arka plan gorevinde yapilir, uygulama acilisini <strong>bloklamaz</strong>.
/// Acilisi uzak bir sunucunun cevap suresine baglamak kabul edilemez: erisilemeyen
/// tek bir MCP sunucusu uygulamayi hic baslatmayabilirdi.
/// </para>
/// <para>
/// Tazeleme araligi <see cref="AgentPrismMcpOptions.RefreshInterval"/> ile
/// belirlenir. Uzak sunucu tool tanimini degistirebilir; onbellek bu araliklarla
/// gerceklikle hizalanir.
/// </para>
/// </remarks>
internal sealed class McpDiscoveryService : BackgroundService
{
    private readonly McpToolCatalog _catalog;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly ISingletonLeaseStore _leaseStore;
    private readonly IOptionsMonitor<SingletonExecutionOptions> _singletonOptionsMonitor;
    private readonly SchemaReadyGate _schemaReadyGate;
    private readonly ILogger<McpDiscoveryService> _logger;

    public McpDiscoveryService(
        McpToolCatalog catalog,
        IOptions<AgentPrismMcpOptions> options,
        ISingletonLeaseStore leaseStore,
        IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
        SchemaReadyGate schemaReadyGate,
        ILogger<McpDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(singletonOptionsMonitor);
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _options = options;
        _leaseStore = leaseStore;
        _singletonOptionsMonitor = singletonOptionsMonitor;
        _schemaReadyGate = schemaReadyGate;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("MCP tool kesfi kapali (AgentPrism:Mcp:Enabled = false).");
            return;
        }

        var interval = _options.Value.RefreshInterval;

        // 🚨 Ilk SQL denemesinden ONCE semanin hazir olmasini bekle. Kayit sirasi
        // `.UseMcp()` `.UseSqlite()`'tan onceyse migration henuz bitmemis olabilir
        // ve ilk tur "no such table" verirdi. Olculdu (Faz 42); gerekce K-354.
        try
        {
            await _schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Tek yurutucu secimi (Faz 42): kapaliysa (varsayilan) guard.IsHeld
        // daima true'dur ve RunAsync depoya hicbir sorgu atmadan hemen doner.
        var guard = new SingletonGuard(_leaseStore, _singletonOptionsMonitor, "mcp-discovery", _logger);
        var guardTask = guard.RunAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (guard.IsHeld)
                {
                    try
                    {
                        var count = await _catalog.RefreshAsync(stoppingToken).ConfigureAwait(false);

                        _logger.LogInformation("MCP kesfi tamamlandi: {ToolCount} tool kullanilabilir.", count);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Tazeleme dongusu asla olmemeli: bir hata sonraki turda
                        // yeniden denenir, onbellekteki son iyi liste kullanilmaya
                        // devam eder.
                        _logger.LogError(ex, "MCP kesfi basarisiz oldu; {Interval} sonra yeniden denenecek.", interval);
                    }
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            await guardTask.ConfigureAwait(false);
        }
    }
}
