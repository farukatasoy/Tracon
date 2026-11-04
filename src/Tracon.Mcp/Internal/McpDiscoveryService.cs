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
/// with reality at this interval. The interval is read before every pass, so a
/// change of the configuration takes effect on the next one, and the wait runs on
/// the injected <see cref="TimeProvider"/>.
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
    private readonly IOptionsMonitor<TraconMcpOptions> _options;
    private readonly SchemaReadyGate _schemaReadyGate;
    private readonly ILogger<McpDiscoveryService> _logger;
    private readonly TimeProvider _clock;

    public McpDiscoveryService(
        McpToolCatalog catalog,
        IOptionsMonitor<TraconMcpOptions> options,
        SchemaReadyGate schemaReadyGate,
        ILogger<McpDiscoveryService> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _options = options;
        _schemaReadyGate = schemaReadyGate;
        _logger = logger;
        _clock = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = _options.CurrentValue;

        if (!current.Enabled)
        {
            _logger.LogInformation("MCP tool discovery is disabled (Tracon:Mcp:Enabled = false).");
            return;
        }

        var interval = current.RefreshInterval;

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
                interval = ReadInterval(interval);
                _logger.LogError(ex, "MCP discovery failed; retrying in {Interval}.", interval);
            }

            // Read after the pass, not before it: a pass against an unreachable server can take as long as
            // the connection timeout, and a change made meanwhile applies to the wait that follows.
            interval = ReadInterval(interval);

            try
            {
                await Task.Delay(interval, _clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Reads the refresh interval for the next pass. A value a timer cannot take, or options that cannot be
    /// read (a reload that failed validation), leave the interval the loop already runs on.
    /// </summary>
    /// <param name="current">The interval of the pass before.</param>
    /// <returns>The interval to wait after the next pass.</returns>
    private TimeSpan ReadInterval(TimeSpan current)
    {
        try
        {
            var configured = _options.CurrentValue.RefreshInterval;

            return TimerPeriod.IsValid(configured) ? configured : current;
        }
        catch (OptionsValidationException)
        {
            return current;
        }
    }
}
