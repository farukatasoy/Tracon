namespace Tracon.CapacityDriver;

/// <summary>Decides which measurements a cell actually supplied.</summary>
/// <remarks>
/// <para>
/// 🚨 The rule the whole report rests on: a measurement that could not be taken
/// is listed with its reason, never written as a zero. A zero reads as
/// "measured, and it was nothing", which is a different and unfalsifiable claim.
/// </para>
/// <para>
/// Mandatory and optional are separated on purpose. Without HTTP latency, model
/// turns, process memory and (where something was queued) queue wait, the cell
/// is not a capacity measurement at all and is invalid. Platform-dependent
/// processor, garbage-collection and SQL detail only narrow the coverage.
/// </para>
/// </remarks>
public static class TelemetryCoverageBuilder
{
    public static TelemetryCoverage Build(
        CellResult result,
        HostTelemetry telemetry,
        IReadOnlyList<string> missingProcesses)
    {
        var coverage = new TelemetryCoverage();

        void Record(string name, bool available, string reason)
        {
            if (available)
            {
                coverage.Available.Add(name);
            }
            else
            {
                coverage.Unavailable[name] = reason;
            }
        }

        Record("http.latency", result.LatencyByScenario.Values.Any(static s => s.Count > 0), "no successful request produced a latency sample");
        // 🚨 EITHER source counts, and that is not leniency. On the worker axis
        // the HTTP host does not serve model calls at all - the worker
        // processes do, and they have no telemetry endpoint to ask. Requiring
        // the host's counter marked every worker-axis cell invalid while the
        // turns were sitting, correctly recorded, in the execution log the
        // whole distribution is built from. The measurement was present; the
        // check was looking in one of the two places it can live.
        Record(
            "model.turns",
            telemetry.ModelTurns > 0 || result.Model.Turns > 0,
            "neither the host counter nor the execution log recorded a model turn");
        Record("process.rss", result.Processes.Exists(static p => p.PeakRssBytes > 0), "no process reported a resident set size");
        Record("process.cpu", result.Processes.Exists(static p => p.ProcessorSeconds > 0), "processor time is not readable for these processes on this platform");
        Record("process.gc", result.Processes.Exists(static p => p.GcSeconds is not null), "garbage collection time is only readable for the driver's own process");
        Record("sql.storage", result.Storage.Available, result.Storage.Unavailable ?? "the schema reported no measured table");
        Record("queue.wait", result.QueueWait.Count > 0, "no queued job recorded a start time");

        foreach (var reason in telemetry.Unavailable)
        {
            coverage.Unavailable["host." + reason] = "the host reported this instrument as unavailable";
        }

        foreach (var role in missingProcesses)
        {
            coverage.Unavailable["process." + role] = "the process could not be opened for sampling";
        }

        // Mandatory: without these the run is not a capacity measurement at
        // all. Platform-dependent CPU/GC/SQL detail is optional and its absence
        // only narrows the telemetry coverage.
        string[] mandatory = ["http.latency", "model.turns", "process.rss", "queue.wait"];

        foreach (var name in mandatory)
        {
            if (!coverage.Available.Contains(name, StringComparer.Ordinal))
            {
                // The queue wait is only mandatory where something was queued.
                if (string.Equals(name, "queue.wait", StringComparison.Ordinal)
                    && !result.Scenarios.Contains(CapacityScenario.Queued, StringComparer.Ordinal))
                {
                    continue;
                }

                coverage.MissingMandatory.Add(name);
            }
        }

        return coverage;
    }
}
