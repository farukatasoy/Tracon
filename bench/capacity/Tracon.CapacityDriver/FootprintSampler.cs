using System.Diagnostics;
using Npgsql;

namespace Tracon.CapacityDriver;

/// <summary>One line of <c>footprint.jsonl</c>: everything known about the host process's footprint at one moment.</summary>
/// <remarks>
/// 🚨 A reading that could not be taken is absent from <see cref="Values"/> and named in <see cref="Missing"/>. It is
/// never written as zero: a zero thread count or a zero connection count reads as "measured, and nothing was
/// there", which is the claim a soak exists to make and must not make by accident.
/// </remarks>
public sealed class FootprintSample
{
    /// <summary>The cell being measured.</summary>
    public string CellId { get; set; } = "";

    /// <summary>Seconds from the window's start.</summary>
    public double OffsetSeconds { get; set; }

    /// <summary>The ramp stage at the moment of the sample.</summary>
    public int Stage { get; set; }

    /// <summary>One of <see cref="RampPhase"/>.</summary>
    public string Phase { get; set; } = "";

    /// <summary>Ramp tenants that had joined.</summary>
    public int Tenants { get; set; }

    /// <summary>Requests issued so far.</summary>
    public long Issued { get; set; }

    /// <summary>Whether the host was asked for a full collection first, so <c>host.liveHeapBytes</c> is present.</summary>
    public bool Collected { get; set; }

    /// <summary>The numeric readings by name.</summary>
    public Dictionary<string, double> Values { get; set; } = [];

    /// <summary>Distinct metric series per instrument; present on collected samples only.</summary>
    public Dictionary<string, long>? InstrumentSeries { get; set; }

    /// <summary>The sources that failed this time, with the reason.</summary>
    public List<string>? Missing { get; set; }
}

/// <summary>Samples the host's footprint, and the database's view of its connections, at a fixed interval.</summary>
/// <remarks>
/// <para>
/// The host reports its own counters (managed heap, thread pool, timers, descriptors, metric series) because the
/// runtime exposes them per process and the driver cannot read another process's. The database side is read here,
/// from <c>pg_stat_activity</c>, because a connection that was opened and never returned is something the server
/// knows and the host's own accounting may not.
/// </para>
/// <para>
/// Every read has a deadline. A host that stops answering the diagnostics route is itself a finding, and the
/// sampler records the gap and carries on rather than waiting on it.
/// </para>
/// </remarks>
public sealed class FootprintSampler : IAsyncDisposable
{
    /// <summary>The application name the driver's own connections carry, so the server count can leave them out.</summary>
    public const string DriverApplicationName = "tracon-capacity-driver";

    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(15);

    private readonly string _cellId;
    private readonly HostProbe _probe;
    private readonly string _connectionString;
    private readonly BoundedJsonlWriter<FootprintSample> _writer;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _collectInterval;
    private readonly TenantRampSchedule _schedule;
    private readonly Func<double> _clock;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private double _lastCollect = double.NegativeInfinity;
    private NpgsqlConnection? _session;

    /// <summary>Creates a sampler.</summary>
    /// <param name="cellId">The cell being measured.</param>
    /// <param name="probe">The probe on the host.</param>
    /// <param name="connectionString">How to reach the cell's database.</param>
    /// <param name="writer">Where the samples go.</param>
    /// <param name="interval">How often to sample.</param>
    /// <param name="collectInterval">How often to force a collection; zero never does.</param>
    /// <param name="schedule">The ramp, which says what stage and phase each sample belongs to.</param>
    /// <param name="clock">Seconds since the window opened.</param>
    public FootprintSampler(
        string cellId,
        HostProbe probe,
        string connectionString,
        BoundedJsonlWriter<FootprintSample> writer,
        TimeSpan interval,
        TimeSpan collectInterval,
        TenantRampSchedule schedule,
        Func<double> clock)
    {
        _cellId = cellId;
        _probe = probe;
        _connectionString = connectionString;
        _writer = writer;
        _interval = interval;
        _collectInterval = collectInterval;
        _schedule = schedule;
        _clock = clock;
    }

    /// <summary>How many samples were written.</summary>
    public int Samples { get; private set; }

    /// <summary>How many samples lacked at least one source.</summary>
    public int Gaps { get; private set; }

    /// <summary>Starts sampling.</summary>
    public void Start() => _loop = Task.Run(LoopAsync);

    /// <summary>Takes one last sample with a forced collection, then stops.</summary>
    /// <returns>A task that completes when the loop has ended and the last sample is written.</returns>
    public async Task StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Stopping is how this loop ends.
            }
        }

        // The closing reading is always collected: it is the one that says what the process still holds.
        await SampleOnceAsync(forceCollect: true, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _stop.Dispose();

        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();

            try
            {
                await SampleOnceAsync(forceCollect: false, _stop.Token).ConfigureAwait(false);
                var spent = Stopwatch.GetElapsedTime(started);
                var wait = _interval - spent;

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task SampleOnceAsync(bool forceCollect, CancellationToken cancellationToken)
    {
        var offset = _clock();
        var collect = forceCollect
            || (_collectInterval > TimeSpan.Zero && offset - _lastCollect >= _collectInterval.TotalSeconds);

        if (collect)
        {
            _lastCollect = offset;
        }

        var sample = new FootprintSample
        {
            CellId = _cellId,
            OffsetSeconds = Math.Round(offset, 3),
            Stage = _schedule.Stage,
            Phase = _schedule.Phase,
            Tenants = _schedule.TenantsJoined,
            Issued = _schedule.Issued,
            Collected = collect,
        };

        var host = ReadHostAsync(collect, cancellationToken);
        var database = ReadDatabaseAsync(cancellationToken);

        var hostResult = await host.ConfigureAwait(false);
        var databaseResult = await database.ConfigureAwait(false);

        if (hostResult.Diagnostics is { } diagnostics)
        {
            foreach (var (name, value) in diagnostics.Values)
            {
                sample.Values[name] = value;
            }

            sample.InstrumentSeries = diagnostics.InstrumentSeries is null ? null : new Dictionary<string, long>(diagnostics.InstrumentSeries);
        }

        if (databaseResult.Counts is { } counts)
        {
            foreach (var (name, value) in counts)
            {
                sample.Values[name] = value;
            }
        }

        var missing = new List<string>();

        if (hostResult.Error is not null)
        {
            missing.Add("host: " + hostResult.Error);
        }

        if (databaseResult.Error is not null)
        {
            missing.Add("pg: " + databaseResult.Error);
        }

        if (missing.Count > 0)
        {
            sample.Missing = missing;
            Gaps++;
        }

        Samples++;
        await _writer.WriteAsync(sample).ConfigureAwait(false);
    }

    private async Task<(HostDiagnostics? Diagnostics, string? Error)> ReadHostAsync(bool collect, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(collect ? TimeSpan.FromSeconds(60) : ReadDeadline);

        try
        {
            return (await _probe.ReadDiagnosticsAsync(collect, deadline.Token).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "no answer inside the deadline");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return (null, Redactor.Scrub(ex.Message));
        }
    }

    private async Task<(Dictionary<string, double>? Counts, string? Error)> ReadDatabaseAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadDeadline);

        try
        {
            // One session for the whole cell: opening a connection a second would be a cost of its own on a
            // database whose connection count is the thing being read.
            var connection = await SessionAsync(deadline.Token).ConfigureAwait(false);

            await using var command = connection.CreateCommand();

            // The host's pool and its worker, and nothing else: the driver's own sessions carry their own name, and
            // the autovacuum workers and the like are not client backends.
            command.CommandText =
                """
                SELECT count(*),
                       count(*) FILTER (WHERE state = 'active'),
                       count(*) FILTER (WHERE state = 'idle'),
                       count(*) FILTER (WHERE state LIKE 'idle in transaction%')
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND backend_type = 'client backend'
                  AND application_name <> @driver
                """;
            command.Parameters.AddWithValue("driver", DriverApplicationName);

            await using var reader = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);

            if (!await reader.ReadAsync(deadline.Token).ConfigureAwait(false))
            {
                return (null, "pg_stat_activity returned no row");
            }

            return (
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["pg.connections"] = reader.GetInt64(0),
                    ["pg.active"] = reader.GetInt64(1),
                    ["pg.idle"] = reader.GetInt64(2),
                    ["pg.idleInTransaction"] = reader.GetInt64(3),
                },
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "no answer inside the deadline");
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException)
        {
            // The session may be broken; the next sample opens a fresh one.
            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }

            return (null, Redactor.Scrub(ex.Message));
        }
    }

    private async Task<NpgsqlConnection> SessionAsync(CancellationToken cancellationToken)
    {
        if (_session is { State: System.Data.ConnectionState.Open })
        {
            return _session;
        }

        if (_session is not null)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }

        var builder = new NpgsqlConnectionStringBuilder(_connectionString) { ApplicationName = DriverApplicationName, Pooling = false };
        _session = new NpgsqlConnection(builder.ConnectionString);
        await _session.OpenAsync(cancellationToken).ConfigureAwait(false);
        return _session;
    }
}
