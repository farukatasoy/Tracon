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
/// <para>
/// <strong>Every instance discovers, also while single-executor selection is on.</strong>
/// The catalog is a cache of the process and holds the live connection a tool call goes through;
/// every instance needs both, so a lease that let one instance discover left the others with no MCP
/// tool at all (measured: 1 tool on the leader, 0 on the other instance). The cost is one connection
/// per server and one listing per refresh interval on every instance; with two instances that is
/// small.
/// </para>
/// </remarks>
internal sealed class McpDiscoveryService : BackgroundService
{
    private readonly McpToolCatalog _catalog;
    private readonly IOptions<TraconMcpOptions> _options;
    private readonly SchemaReadyGate _schemaReadyGate;
    private readonly ILogger<McpDiscoveryService> _logger;

    public McpDiscoveryService(
        McpToolCatalog catalog,
        IOptions<TraconMcpOptions> options,
        SchemaReadyGate schemaReadyGate,
        ILogger<McpDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _options = options;
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
        // "no such table". Measured.
        try
        {
            await _schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
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
}
