using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Tracon.Capacity;

namespace Tracon.CapacityDriver;

/// <summary>Runs one measurement cell end to end.</summary>
/// <remarks>
/// <para>
/// The shape is always the same: warm-up, reset, measured window, drain,
/// reconcile. 🚨 The four phases are kept apart on purpose. Warm-up samples
/// never enter a percentile; the drain never enters the throughput denominator;
/// and the reconciliation happens after the drain, because a run that is still
/// advancing has nothing to reconcile yet.
/// </para>
/// </remarks>
public sealed class CellRunner
{
    private readonly CellSpec _spec;
    private readonly HttpClient _client;
    private readonly string _connectionString;
    private readonly string _executionLogDirectory;
    private readonly TextWriter _log;

    /// <summary>Creates a runner for one cell.</summary>
    /// <param name="spec">The cell.</param>
    /// <param name="client">The HTTP client pointed at the host.</param>
    /// <param name="connectionString">How to reach the database, read-only.</param>
    /// <param name="executionLogDirectory">Where the host processes wrote their execution records.</param>
    /// <param name="log">Where progress is written.</param>
    public CellRunner(
        CellSpec spec,
        HttpClient client,
        string connectionString,
        string executionLogDirectory,
        TextWriter log)
    {
        _spec = spec;
        _client = client;
        _connectionString = connectionString;
        _executionLogDirectory = executionLogDirectory;
        _log = log;
    }

    /// <summary>Runs the cell.</summary>
    /// <param name="cancellationToken">Cancels the cell; an interrupted cell still produces a partial result.</param>
    /// <returns>The result.</returns>
    public async Task<CellResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new CellResult
        {
            RunId = _spec.RunId,
            CellId = _spec.CellId,
            Profile = _spec.Profile,
            Scenarios = [.. _spec.Scenarios],
            SeedShape = _spec.SeedShape,
            Repeat = _spec.Repeat,
            Concurrency = _spec.Concurrency,
            ArrivalRatePerSecond = _spec.ArrivalRatePerSecond,
            WorkerCount = _spec.WorkerCount,
        };

        Directory.CreateDirectory(_spec.OutputDirectory);

        var probe = new HostProbe(_client, _spec.BaseAddress);
        var settings = await probe.ReadSettingsAsync(cancellationToken).ConfigureAwait(false);

        // 🚨 The worker axis refuses to start rather than produce a shifted
        // series: with the host's own worker still leasing, "1 worker" is two.
        if (_spec.WorkerCount is not null && settings.RunWorker)
        {
            result.Invalidate(
                "the host's effective Scheduling:RunWorker is true, so the worker axis would be shifted by one");
            await WriteAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }

        var accountant = new StorageAccountant(_connectionString, _spec.Schema);
        var reconciler = new StoreReconciler(_connectionString, _spec.Schema);

        await using var requests = new BoundedJsonlWriter<RequestSample>(
            Path.Combine(_spec.OutputDirectory, "requests.jsonl"),
            CapacityJson.Default.RequestSample,
            _spec.Limits.MaxRequestSamples);

        await using var resources = new BoundedJsonlWriter<ResourceSample>(
            Path.Combine(_spec.OutputDirectory, "resources.jsonl"),
            CapacityJson.Default.ResourceSample,
            _spec.Limits.MaxRequestSamples);

        await using var sampler = new ResourceSampler(
            _spec.CellId,
            resources,
            TimeSpan.FromSeconds(_spec.ResourceSampleIntervalSeconds));

        var missingProcesses = new List<string>();

        if (!sampler.Track("driver", Environment.ProcessId))
        {
            missingProcesses.Add("driver");
        }
        if (!sampler.Track("host", _spec.HostPid))
        {
            missingProcesses.Add("host");
        }

        for (var i = 0; i < _spec.WorkerPids.Count; i++)
        {
            var role = "worker-" + (i + 1).ToString(CultureInfo.InvariantCulture);

            if (!sampler.Track(role, _spec.WorkerPids[i]))
            {
                missingProcesses.Add(role);
            }
        }

        var client = new ScenarioClient(_client, _spec);
        var samples = new ConcurrentBag<RequestSample>();

        // Warm-up. Its samples are written to the raw file (so the reader can
        // see them) but carry the warmup flag and never enter a statistic.
        if (_spec.WarmupSeconds > 0)
        {
            _log.WriteLine($"[{_spec.CellId}] warm-up {_spec.WarmupSeconds:0.#}s");
            await ClosedLoopAsync(
                client,
                requests,
                samples: null,
                TimeSpan.FromSeconds(_spec.WarmupSeconds),
                runsPerTenant: 0,
                warmup: true,
                cancellationToken).ConfigureAwait(false);
        }

        await probe.ResetAsync(cancellationToken).ConfigureAwait(false);
        var storageBefore = await accountant.ReadAsync(cancellationToken).ConfigureAwait(false);

        result.WindowStartUtc = DateTimeOffset.UtcNow;
        sampler.Start();
        var windowStarted = Stopwatch.GetTimestamp();

        var limitBreach = await MeasureAsync(
            client,
            requests,
            samples,
            accountant,
            sampler,
            result,
            cancellationToken).ConfigureAwait(false);

        result.MeasuredSeconds = Math.Round(Stopwatch.GetElapsedTime(windowStarted).TotalSeconds, 3);

        if (limitBreach is not null)
        {
            result.Incomplete(CellStatus.ResourceLimit);
            result.StatusReasons.Add(limitBreach);
        }

        // Drain. Nothing new enters; what was accepted is followed to a
        // terminal status so the eventual outcome of the cohort is known.
        var drainStarted = Stopwatch.GetTimestamp();
        var drained = await DrainAsync(samples, reconciler, cancellationToken).ConfigureAwait(false);
        result.DrainSeconds = Math.Round(Stopwatch.GetElapsedTime(drainStarted).TotalSeconds, 3);

        if (!drained)
        {
            result.Incomplete(CellStatus.DrainLimit);
        }

        var processSummaries = await sampler.StopAsync().ConfigureAwait(false);
        result.Processes = processSummaries;

        var storageAfter = await accountant.ReadAsync(cancellationToken).ConfigureAwait(false);

        var measured = samples.Where(static s => !s.Warmup).ToList();
        var cohort = measured
            .Where(static s => s.Correlation.Length > 0)
            .Select(static s => s.Correlation)
            .ToHashSet(StringComparer.Ordinal);

        await SummariseAsync(
            result,
            client,
            measured,
            cohort,
            reconciler,
            storageBefore,
            storageAfter,
            settings,
            probe,
            missingProcesses,
            cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            result.Incomplete(CellStatus.Interrupted);
        }

        await WriteAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<string?> MeasureAsync(
        ScenarioClient client,
        BoundedJsonlWriter<RequestSample> requests,
        ConcurrentBag<RequestSample> samples,
        StorageAccountant accountant,
        ResourceSampler sampler,
        CellResult result,
        CancellationToken cancellationToken)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 🚨 The watchdog is a SAFETY net, not a measurement. A failure inside
        // it must not destroy a cell that is otherwise producing valid samples,
        // so its own error is turned into a warning on the result.
        var watchdog = Task.Run(async () =>
        {
            try
            {
                return await WatchResourcesAsync(accountant, sampler, window, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Warnings.Add("the resource watchdog failed: " + Redactor.Scrub(ex.Message));
                return null;
            }
        });

        if (_spec.ArrivalRatePerSecond is { } rate)
        {
            await OpenLoopAsync(client, requests, samples, rate, result, window.Token).ConfigureAwait(false);
        }
        else
        {
            await ClosedLoopAsync(
                client,
                requests,
                samples,
                _spec.MeasureSeconds > 0 ? TimeSpan.FromSeconds(_spec.MeasureSeconds) : Timeout.InfiniteTimeSpan,
                _spec.RunsPerTenant,
                warmup: false,
                window.Token).ConfigureAwait(false);
        }

        await window.CancelAsync().ConfigureAwait(false);
        return await watchdog.ConfigureAwait(false);
    }

    private async Task<string?> WatchResourcesAsync(
        StorageAccountant accountant,
        ResourceSampler sampler,
        CancellationTokenSource window,
        CancellationToken cancellationToken)
    {
        while (!window.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), window.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (window.IsCancellationRequested)
            {
                return null;
            }

            if (sampler.PeakRssBytes.TryGetValue("host", out var hostRss) && hostRss > _spec.Limits.HostRssBytes)
            {
                await window.CancelAsync().ConfigureAwait(false);
                return $"host RSS {hostRss} exceeded the cap {_spec.Limits.HostRssBytes}";
            }

            if (sampler.PeakRssBytes.TryGetValue("driver", out var driverRss) && driverRss > _spec.Limits.DriverRssBytes)
            {
                await window.CancelAsync().ConfigureAwait(false);
                return $"driver RSS {driverRss} exceeded the cap {_spec.Limits.DriverRssBytes}";
            }

            long schemaBytes;

            try
            {
                schemaBytes = await accountant.TotalBytesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Npgsql.NpgsqlException)
            {
                // The size probe is not a measurement the cell depends on; a
                // transient failure must not end the window on its own.
                continue;
            }

            if (schemaBytes > _spec.Limits.DatabaseBytes)
            {
                await window.CancelAsync().ConfigureAwait(false);
                return $"schema size {schemaBytes} exceeded the cap {_spec.Limits.DatabaseBytes}";
            }

            var free = FreeDiskBytes(_spec.OutputDirectory);

            if (free is { } bytes && bytes < _spec.Limits.MinimumFreeDiskBytes)
            {
                await window.CancelAsync().ConfigureAwait(false);
                return $"free disk {bytes} fell below the floor {_spec.Limits.MinimumFreeDiskBytes}";
            }
        }

        return null;
    }

    private async Task ClosedLoopAsync(
        ScenarioClient client,
        BoundedJsonlWriter<RequestSample> requests,
        ConcurrentBag<RequestSample>? samples,
        TimeSpan duration,
        int runsPerTenant,
        bool warmup,
        CancellationToken cancellationToken)
    {
        var deadline = duration == Timeout.InfiniteTimeSpan
            ? (long?)null
            : Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        var budget = runsPerTenant > 0
            ? runsPerTenant * _spec.Tenants.Count * _spec.Scenarios.Count
            : int.MaxValue;

        var issued = 0;
        var workers = new Task[_spec.Concurrency];

        for (var slot = 0; slot < workers.Length; slot++)
        {
            var offset = slot;

            workers[slot] = Task.Run(
                async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (deadline is { } limit && Stopwatch.GetTimestamp() >= limit)
                        {
                            return;
                        }

                        var index = Interlocked.Increment(ref issued) - 1;

                        if (index >= budget)
                        {
                            return;
                        }

                        var scenario = _spec.Scenarios[(index + offset) % _spec.Scenarios.Count];
                        var tenant = _spec.Tenants[index % _spec.Tenants.Count];
                        var correlation = NewCorrelation();

                        var sample = await client
                            .ExecuteAsync(scenario, tenant, correlation, warmup, cancellationToken)
                            .ConfigureAwait(false);

                        sample.CompletedInWindow = deadline is null || Stopwatch.GetTimestamp() < deadline;

                        await requests.WriteAsync(sample).ConfigureAwait(false);
                        samples?.Add(sample);
                    }
                },
                CancellationToken.None);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task OpenLoopAsync(
        ScenarioClient client,
        BoundedJsonlWriter<RequestSample> requests,
        ConcurrentBag<RequestSample> samples,
        double rate,
        CellResult result,
        CancellationToken cancellationToken)
    {
        var schedule = ArrivalSchedule.Fixed(rate, _spec.MeasureSeconds);
        var slots = new SemaphoreSlim(_spec.Limits.MaxInFlight, _spec.Limits.MaxInFlight);
        var inFlight = new List<Task>(schedule.Planned);
        var started = Stopwatch.GetTimestamp();
        var notSent = 0;
        var scenario = CapacityScenario.Queued;

        for (var index = 0; index < schedule.Planned; index++)
        {
            var due = schedule.DueSeconds(index);
            var wait = due - Stopwatch.GetElapsedTime(started).TotalSeconds;

            if (wait > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            // 🚨 The plan is never deferred into a lower rate. When no slot is
            // free at the due moment the request is recorded as not sent, with
            // its due time, so the report shows the load that was OFFERED next
            // to the load that was actually applied.
            if (!await slots.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                notSent++;

                var skipped = new RequestSample
                {
                    CellId = _spec.CellId,
                    Scenario = scenario,
                    Tenant = _spec.Tenants[index % _spec.Tenants.Count],
                    Correlation = "",
                    DispatchedUtc = DateTimeOffset.UtcNow,
                    DueSeconds = Math.Round(due, 4),
                    Outcome = RequestOutcome.NotSent,
                };

                await requests.WriteAsync(skipped).ConfigureAwait(false);
                samples.Add(skipped);
                continue;
            }

            var tenant = _spec.Tenants[index % _spec.Tenants.Count];
            var correlation = NewCorrelation();
            var lag = Stopwatch.GetElapsedTime(started).TotalSeconds - due;

            inFlight.Add(Task.Run(
                async () =>
                {
                    try
                    {
                        var sample = await client
                            .ExecuteAsync(scenario, tenant, correlation, warmup: false, cancellationToken)
                            .ConfigureAwait(false);

                        sample.DueSeconds = Math.Round(due, 4);
                        sample.DispatchLagMilliseconds = Math.Round(lag * 1000, 3);
                        sample.CompletedInWindow = Stopwatch.GetElapsedTime(started).TotalSeconds <= _spec.MeasureSeconds;

                        await requests.WriteAsync(sample).ConfigureAwait(false);
                        samples.Add(sample);
                    }
                    finally
                    {
                        slots.Release();
                    }
                },
                CancellationToken.None));
        }

        await Task.WhenAll(inFlight).ConfigureAwait(false);
        result.Arrival.Planned = schedule.Planned;
        result.Arrival.NotSent = notSent;
        slots.Dispose();
    }

    private async Task<bool> DrainAsync(
        ConcurrentBag<RequestSample> samples,
        StoreReconciler reconciler,
        CancellationToken cancellationToken)
    {
        var pending = samples
            .Where(static s => !s.Warmup && s.RunId is not null)
            .Select(static s => s.RunId!)
            .Distinct(StringComparer.Ordinal)
            .Select(static id => Guid.Parse(id))
            .ToList();

        if (pending.Count == 0)
        {
            return true;
        }

        var deadline = Stopwatch.GetTimestamp() + (long)(_spec.DrainTimeoutSeconds * Stopwatch.Frequency);
        var byTenant = new Dictionary<string, IReadOnlyList<Guid>> { [""] = pending };

        while (Stopwatch.GetTimestamp() < deadline && !cancellationToken.IsCancellationRequested)
        {
            var cohort = await reconciler.ReconcileAsync(byTenant, cancellationToken).ConfigureAwait(false);

            if (cohort.Terminal + cohort.Missing >= pending.Count)
            {
                return true;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        return false;
    }

    private async Task SummariseAsync(
        CellResult result,
        ScenarioClient client,
        List<RequestSample> measured,
        HashSet<string> cohort,
        StoreReconciler reconciler,
        IReadOnlyDictionary<string, StorageAccountant.TableReading> storageBefore,
        IReadOnlyDictionary<string, StorageAccountant.TableReading> storageAfter,
        HostSettings settings,
        HostProbe probe,
        IReadOnlyList<string> missingProcesses,
        CancellationToken cancellationToken)
    {
        var sent = measured.Where(static s => !string.Equals(s.Outcome, RequestOutcome.NotSent, StringComparison.Ordinal)).ToList();

        result.Arrival.Planned = result.Arrival.Planned == 0 ? measured.Count : result.Arrival.Planned;
        result.Arrival.Sent = sent.Count;
        result.Arrival.NotSent = measured.Count - sent.Count > result.Arrival.NotSent
            ? measured.Count - sent.Count
            : result.Arrival.NotSent;
        result.Arrival.Accepted = sent.Count(static s => string.Equals(s.Outcome, RequestOutcome.Accepted, StringComparison.Ordinal));
        result.Arrival.Rejected = sent.Count(static s => string.Equals(s.Outcome, RequestOutcome.Rejected, StringComparison.Ordinal));
        result.Arrival.Failed = sent.Count(static s => string.Equals(s.Outcome, RequestOutcome.Failed, StringComparison.Ordinal));
        result.Arrival.TimedOut = sent.Count(static s => string.Equals(s.Outcome, RequestOutcome.TimedOut, StringComparison.Ordinal));
        result.Arrival.DispatchLag = LatencyStatistics
            .From(measured.Where(static s => s.DispatchLagMilliseconds is not null).Select(static s => s.DispatchLagMilliseconds!.Value))
            .ToSummary();

        var succeeded = measured
            .Where(static s => string.Equals(s.Outcome, RequestOutcome.Accepted, StringComparison.Ordinal))
            .ToList();

        result.Arrival.Completed = succeeded.Count;

        var completedInWindow = succeeded.Count(static s => s.CompletedInWindow);
        result.ThroughputPerSecond = result.MeasuredSeconds > 0
            ? Math.Round(completedInWindow / result.MeasuredSeconds, 4)
            : 0;

        foreach (var scenario in _spec.Scenarios)
        {
            var forScenario = succeeded.Where(s => string.Equals(s.Scenario, scenario, StringComparison.Ordinal)).ToList();

            result.LatencyByScenario[scenario] = LatencyStatistics
                .From(forScenario.Where(static s => s.TotalMilliseconds is not null).Select(static s => s.TotalMilliseconds!.Value))
                .ToSummary();

            result.FirstContentByScenario[scenario] = LatencyStatistics
                .From(forScenario.Where(static s => s.FirstContentMilliseconds is not null).Select(static s => s.FirstContentMilliseconds!.Value))
                .ToSummary();
        }

        var runIdsByTenant = succeeded
            .Where(static s => s.RunId is not null)
            .GroupBy(static s => s.Tenant, StringComparer.Ordinal)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<Guid>)g.Select(static s => Guid.Parse(s.RunId!)).Distinct().ToList(),
                StringComparer.Ordinal);

        var allRunIds = runIdsByTenant.SelectMany(static p => p.Value).ToList();
        var storeCohort = await reconciler.ReconcileAsync(runIdsByTenant, cancellationToken).ConfigureAwait(false);
        var gaps = await reconciler.SequenceGapsAsync(allRunIds, cancellationToken).ConfigureAwait(false);

        result.Reconciliation = new ReconciliationSummary
        {
            AcceptedRunIds = allRunIds.Count,
            TerminalRuns = storeCohort.Terminal,
            MissingRuns = storeCohort.Missing,
            ContentMismatches = succeeded.Count(static s => s.ContentMatched == false),
            SequenceGaps = gaps,
            LiveSubscriptions = succeeded.Count(static s => s.LiveSubscription == true),
            HistoricalOnlySubscriptions = succeeded.Count(static s => s.LiveSubscription == false),
            TenantBleed = storeCohort.TenantBleed,
            Complete = true,
        };

        result.Reconciliation.CrossTenantRefusals = await CountCrossTenantRefusalsAsync(
            client, runIdsByTenant, cancellationToken).ConfigureAwait(false);

        result.QueueWait = LatencyStatistics.From(storeCohort.QueueWaitMilliseconds).ToSummary();

        result.Storage = StorageAccountant.Difference(
            storageBefore,
            storageAfter,
            storeCohort.Terminal,
            storeCohort.Events,
            settings.RetentionEnabled);

        if (result.Storage.VacuumInterference)
        {
            result.Warnings.Add("storage/vacuum-interference: autovacuum ran inside the window; byte growth is not comparable and must stay out of any average");
        }

        var executions = CapacityExecutionLog.ReadAll(_executionLogDirectory);
        result.Model = WorkerAnalysis.SummariseModel(executions, cohort);

        if (_spec.WorkerCount is { } workerCount)
        {
            var workers = WorkerAnalysis.Summarise(
                executions, cohort, workerCount, settings.RunWorker, allRunIds.Count);
            result.Workers = workers;

            if (workers.ExecutedJobs < workers.AcceptedJobs)
            {
                result.Warnings.Add(
                    $"{workers.AcceptedJobs - workers.ExecutedJobs} accepted job(s) had not run when the window closed; the queue was still holding work");
            }

            if (workers.ConcurrentOverlaps > 0)
            {
                result.Invalidate(
                    $"{workers.ConcurrentOverlaps} attempt pair(s) of the same job overlapped in time; at-least-once allows a repeat, not a concurrent execution");
            }

            if (!workers.ContentionMeasured && workerCount > 1)
            {
                result.Warnings.Add(
                    "contention could not be measured: one worker took at least 90% of the jobs, so no scaling claim is made");
            }
        }

        var telemetry = await probe.ReadTelemetryAsync(cancellationToken).ConfigureAwait(false);
        result.Telemetry = TelemetryCoverageBuilder.Build(result, telemetry, missingProcesses);

        if (result.Telemetry.MissingMandatory.Count > 0)
        {
            result.Invalidate("mandatory measurements are missing: " + string.Join(", ", result.Telemetry.MissingMandatory));
        }

        if (result.Reconciliation.MissingRuns > 0)
        {
            result.Invalidate($"{result.Reconciliation.MissingRuns} accepted run id(s) are not in the store");
        }

        if (result.Reconciliation.ContentMismatches > 0)
        {
            result.Invalidate($"{result.Reconciliation.ContentMismatches} answer(s) did not match the expected content");
        }

        if (result.Reconciliation.SequenceGaps > 0)
        {
            result.Invalidate($"{result.Reconciliation.SequenceGaps} recorded stream(s) had a sequence gap");
        }

        if (result.Reconciliation.TenantBleed > 0)
        {
            result.Invalidate($"{result.Reconciliation.TenantBleed} run(s) were recorded under the wrong tenant");
        }

        // Both tenants produced runs in a healthy cell, so both cross-tenant
        // reads must have been refused. A cell where one succeeded has lost
        // isolation under load, whatever its latency said.
        if (runIdsByTenant.Count > 1 && result.Reconciliation.CrossTenantRefusals < runIdsByTenant.Count)
        {
            result.Invalidate("a cross-tenant read was answered instead of refused");
        }

        if (telemetry.RecordingFailures > 0)
        {
            result.Invalidate($"the host reported {telemetry.RecordingFailures} recording failure(s); the run continued but the measurement cannot be trusted");
        }

        if (!result.Arrival.Balances())
        {
            result.Invalidate("the request counters do not balance; the driver lost track of requests");
        }

        if (_spec.Scenarios.Contains(CapacityScenario.Queued, StringComparer.Ordinal)
            && result.Reconciliation.LiveSubscriptions == 0
            && result.Reconciliation.HistoricalOnlySubscriptions > 0)
        {
            result.Warnings.Add("historical-only: no event subscriber attached while a run was still live");
        }
    }

    private async Task<long> CountCrossTenantRefusalsAsync(
        ScenarioClient client,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> runIdsByTenant,
        CancellationToken cancellationToken)
    {
        long refusals = 0;

        foreach (var (tenant, ids) in runIdsByTenant)
        {
            if (ids.Count == 0)
            {
                continue;
            }

            var foreign = _spec.Tenants.Find(candidate => !string.Equals(candidate, tenant, StringComparison.Ordinal));

            if (foreign is null)
            {
                continue;
            }

            if (await client
                .ProbeCrossTenantAsync(ids[0].ToString(), foreign, cancellationToken)
                .ConfigureAwait(false))
            {
                refusals++;
            }
        }

        return refusals;
    }

    private async Task WriteAsync(CellResult result, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_spec.OutputDirectory, "cell.json");
        var json = JsonSerializer.Serialize(result, CapacityJson.Default.CellResult);
        await File.WriteAllTextAsync(path, Redactor.Scrub(json), cancellationToken).ConfigureAwait(false);
    }

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static long? FreeDiskBytes(string path)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
