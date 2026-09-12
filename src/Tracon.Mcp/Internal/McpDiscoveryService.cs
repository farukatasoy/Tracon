using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// A service that refreshes the tool list of remote MCP servers in the background.
/// </summary>
/// <remarks>
/// <para>
/// The initial discovery happens in a background task and <strong>does not
/// block</strong> application startup. Tying startup to a remote server's
/// response time is unacceptable: a single unreachable MCP server could keep
/// the application from starting at all.
/// </para>
/// <para>
/// The refresh interval is set by <see cref="TraconMcpOptions.RefreshInterval"/>.
/// A remote server may change its tool definitions; the cache is realigned
/// with reality at this interval.
/// </para>
/// </remarks>
internal sealed class McpDiscoveryService : BackgroundService
{
    private readonly McpToolCatalog _catalog;
    private readonly IOptions<TraconMcpOptions> _options;
    private readonly ISingletonLeaseStore _leaseStore;
    private readonly IOptionsMonitor<SingletonExecutionOptions> _singletonOptionsMonitor;
    private readonly SchemaReadyGate _schemaReadyGate;
    private readonly ILogger<McpDiscoveryService> _logger;

    public McpDiscoveryService(
        McpToolCatalog catalog,
        IOptions<TraconMcpOptions> options,
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
            _logger.LogInformation("MCP tool discovery is disabled (Tracon:Mcp:Enabled = false).");
            return;
        }

        var interval = _options.Value.RefreshInterval;

        // 🚨 Wait for the schema to be ready BEFORE the first SQL attempt. If
        // registration order puts `.UseMcp()` before `.UseSqlite()`, the
        // migration may not have finished yet and the first pass would get
        // "no such table". Measured (Phase 42); rationale K-354.
        try
        {
            await _schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Single-executor selection (Phase 42): when disabled (the default),
        // guard.IsHeld is always true and RunAsync returns immediately
        // without issuing any query to the store.
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
                        var outcome = await _catalog.RefreshAsync(stoppingToken).ConfigureAwait(false);

                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation(
                                "MCP discovery completed: {ToolCount} tools available.",
                                outcome.ToolCount);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // The refresh loop must never die: on error, the next
                        // pass retries and the last good list in the cache
                        // keeps being used.
                        _logger.LogError(ex, "MCP discovery failed; retrying in {Interval}.", interval);
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
