// The Tracon capacity driver (Phase 166).
//
// A separate process that calls a packed-consumer host over real loopback TCP
// and writes one cell's measurements, or merges finished cells into a report.
// It has no Tracon dependency: everything it knows about the system under
// measurement it learns through HTTP and read-only SQL.
//
//   Tracon.CapacityDriver cell   --spec <cell.json>
//   Tracon.CapacityDriver report --run  <run directory>
//
// 🚨 The database connection is read from TRACON_CAPACITY_CONNECTION, never
// from an argument: a process argument is visible in a process listing.
using System.Globalization;
using Tracon.Capacity;
using Tracon.CapacityDriver;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: Tracon.CapacityDriver <cell --spec <path> | report --run <directory>>");
    return 2;
}

try
{
    switch (args[0])
    {
        case "cell":
            return await RunCellAsync(Argument(args, "--spec")).ConfigureAwait(false);
        case "report":
            return RunReport(Argument(args, "--run"));
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}'");
            return 2;
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(Redactor.Scrub(ex.Message));
    return 2;
}

static string Argument(string[] args, string name)
{
    for (var index = 1; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    throw new ArgumentException($"{name} is required.", nameof(args));
}

static async Task<int> RunCellAsync(string specPath)
{
    var spec = CellSpec.Load(specPath);
    var problems = spec.Validate();

    if (problems.Count > 0)
    {
        Console.Error.WriteLine("the cell specification cannot be measured:");

        foreach (var problem in problems)
        {
            Console.Error.WriteLine("  " + problem);
        }

        return 2;
    }

    var connection = Environment.GetEnvironmentVariable(CapacityContract.ConnectionVariable);

    if (string.IsNullOrWhiteSpace(connection))
    {
        Console.Error.WriteLine($"{CapacityContract.ConnectionVariable} is not set; the driver never takes a connection string as an argument.");
        return 2;
    }

    var executionLogs = Environment.GetEnvironmentVariable("TRACON_CAPACITY_EXECUTIONS")
        ?? Path.Combine(spec.OutputDirectory, "executions");

    using var interrupt = new CancellationTokenSource();

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        // 🚨 An interrupted cell still produces its partial result. Cancelling
        // the process outright would throw away the samples already taken and
        // leave the started host processes to the orchestrator's cleanup.
        eventArgs.Cancel = true;
        interrupt.Cancel();
    };

    using var handler = new SocketsHttpHandler
    {
        // The driver must be able to hold more sockets than the cell's
        // in-flight cap, or the measurement would be bounded by the client's
        // own connection pool rather than by the server.
        MaxConnectionsPerServer = Math.Max(spec.Limits.MaxInFlight * 2, 64),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    using var client = new HttpClient(handler)
    {
        // Per-request budgets are applied by the scenario client so that a
        // timeout can be recorded as an outcome rather than thrown away.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    var runner = new CellRunner(spec, client, connection, executionLogs, Console.Out);
    var result = await runner.RunAsync(interrupt.Token).ConfigureAwait(false);

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"[{result.CellId}] {result.Status} · {result.Arrival.Completed}/{result.Arrival.Planned} completed · {result.ThroughputPerSecond:0.###}/s"));

    // 🚨 An invalid cell fails the process. Slowness does not: the orchestrator
    // decides whether a slow cell is worth continuing from, but a cell whose
    // numbers cannot be trusted must never look like a successful measurement.
    return string.Equals(result.Status, CellStatus.Invalid, StringComparison.Ordinal) ? 1 : 0;
}

static int RunReport(string runDirectory)
{
    if (!Directory.Exists(runDirectory))
    {
        Console.Error.WriteLine($"'{runDirectory}' does not exist.");
        return 2;
    }

    var summary = ReportBuilder.Build(runDirectory);

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"{summary.Cells} cell(s): {summary.CompleteCells} complete, {summary.IncompleteCells} incomplete, {summary.InvalidCells} invalid"));

    return summary.InvalidCells > 0 ? 1 : 0;
}
