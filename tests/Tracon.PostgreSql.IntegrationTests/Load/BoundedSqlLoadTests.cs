using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests.Load;

/// <summary>
/// A bounded load run against a real PostgreSQL store, and the report it
/// writes (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>This is a report, not a gate.</strong> It asserts nothing about
/// duration. Phase 116 measured that wall-clock time on a shared machine is
/// noise and made ALLOCATION the only performance gate; that decision stands,
/// and turning any number here into a red/green threshold is a separate
/// decision this phase did not take. What the run produces is a file an
/// operator can read, with the environment written next to the numbers - a
/// throughput figure without its machine, database version, payload size and
/// concurrency is not a measurement, it is marketing.
/// </para>
/// <para>
/// 🚨 <strong>Model latency is measured separately from control-plane
/// overhead, and neither is folded into the other.</strong> A "runs per
/// second" number that includes an external model call measures the model, not
/// Tracon; the two phases below are timed apart so the report can say
/// which one the time went to. The stand-in for the model is a fixed delay -
/// it makes no network call and therefore contributes no variance of its own.
/// </para>
/// <para>
/// It is <strong>opt-in</strong>: <c>TRACON_LOAD=1</c>. The run costs tens
/// of seconds and needs a container, and Open Question 2 of the phase decided
/// it is not worth paying on every pull request.
/// </para>
/// </remarks>
public sealed class BoundedSqlLoadTests(PostgresFixture fixture)
{
    private const string OptInVariable = "TRACON_LOAD";

    /// <summary>Concurrent recorders - the "how many runs at once" axis.</summary>
    private const int Concurrency = 8;

    /// <summary>Runs per recorder.</summary>
    private const int RunsPerWorker = 25;

    /// <summary>Streamed deltas per run: the per-event write is the hot path.</summary>
    private const int EventsPerRun = 20;

    /// <summary>The stand-in for one external model call.</summary>
    private static readonly TimeSpan ModelLatency = TimeSpan.FromMilliseconds(20);

    private static readonly string DeltaText = new('x', 256);

    [Fact]
    public async Task Bounded_run_recording_load_produces_a_report()
    {
        Assert.SkipUnless(
            string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.Ordinal),
            $"Set {OptInVariable}=1 to run the bounded load report.");

        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var modelTime = new long[Concurrency];
        var controlPlaneTime = new long[Concurrency];

        var total = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(async worker =>
        {
            var model = new Stopwatch();
            var controlPlane = new Stopwatch();

            for (var i = 0; i < RunsPerWorker; i++)
            {
                var runId = Guid.NewGuid();
                var writer = new RunEventWriter(
                    context.Runs,
                    new TraconRunRecordingOptions(),
                    NullLogger.Instance,
                    runId,
                    metrics: null);

                controlPlane.Start();
                await writer.StartAsync(TestData.Run(runId), "the user's question");
                controlPlane.Stop();

                // The external call. Timed on its own so the report can
                // subtract it rather than claim it.
                model.Start();
                await Task.Delay(ModelLatency);
                model.Stop();

                controlPlane.Start();

                for (var e = 0; e < EventsPerRun; e++)
                {
                    await writer.AppendAsync(new RunEventDraft(RunEventType.MessageDelta) { Text = DeltaText });
                }

                await writer.CompleteAsync(RunStatus.Completed);
                controlPlane.Stop();

                // A writer that quietly gave up would make every number below
                // a measurement of doing nothing.
                writer.IsDisabled.ShouldBeFalse();
            }

            modelTime[worker] = model.ElapsedMilliseconds;
            controlPlaneTime[worker] = controlPlane.ElapsedMilliseconds;
        }));

        total.Stop();

        var runCount = Concurrency * RunsPerWorker;

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.runs;")).ShouldBe(runCount);

        var report = await BuildReportAsync(
            context,
            total.Elapsed,
            TimeSpan.FromMilliseconds(modelTime.Sum()),
            TimeSpan.FromMilliseconds(controlPlaneTime.Sum()),
            runCount);

        var path = await WriteReportAsync(report);

        // Written where a human can read it; the assertion is only that the
        // report exists, since the numbers themselves are not a gate.
        File.Exists(path).ShouldBeTrue();
    }

    private static async Task<string> BuildReportAsync(
        PostgresTestContext context,
        TimeSpan wallClock,
        TimeSpan modelTotal,
        TimeSpan controlPlaneTotal,
        int runCount)
    {
        var eventCount = runCount * EventsPerRun;
        var databaseVersion = await context.ScalarAsync<string>("SELECT version();");
        var commit = await ReadCommitAsync();
        var machine = await ReadMachineAsync();

        // 🚨 Both of these describe the PROCESS, not the machine: the GC figure
        // is the container/GC heap limit and ProcessorCount is the CPU count
        // this process was given. They are labelled as such so nobody compares
        // two runs and blames hardware that the report never named. Phase 166.
        var processMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

        var report = new StringBuilder();

        report.AppendLine("# Bounded SQL load report");
        report.AppendLine();
        report.AppendLine(Invariant($"Produced: {DateTimeOffset.UtcNow:O}"));
        report.AppendLine();
        report.AppendLine("## Environment");
        report.AppendLine();
        report.AppendLine("| Field | Value |");
        report.AppendLine("|---|---|");
        report.AppendLine(Invariant($"| Operating system | {Environment.OSVersion} |"));
        report.AppendLine(Invariant($"| Architecture | {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} |"));
        report.AppendLine(Invariant($"| Processor | {machine.Processor ?? NotAvailable} |"));
        report.AppendLine(Invariant($"| Physical memory | {Describe(machine.PhysicalMemoryBytes)} |"));
        report.AppendLine(Invariant($"| Logical processors (process-visible) | {Environment.ProcessorCount} |"));
        report.AppendLine(Invariant($"| Available memory (process-visible) | {processMemoryBytes / (1024 * 1024)} MiB |"));
        report.AppendLine(Invariant($"| .NET | {Environment.Version} |"));
        report.AppendLine(Invariant($"| Database | {databaseVersion} |"));
        report.AppendLine(Invariant($"| Commit | {commit} |"));
        report.AppendLine();
        report.AppendLine("## Shape");
        report.AppendLine();
        report.AppendLine("| Field | Value |");
        report.AppendLine("|---|---|");
        report.AppendLine(Invariant($"| Concurrency | {Concurrency} |"));
        report.AppendLine(Invariant($"| Runs | {runCount} |"));
        report.AppendLine(Invariant($"| Events per run | {EventsPerRun} |"));
        report.AppendLine(Invariant($"| Delta payload | {DeltaText.Length} characters |"));
        report.AppendLine(Invariant($"| Simulated model latency | {ModelLatency.TotalMilliseconds} ms per run |"));
        report.AppendLine();
        report.AppendLine("## Measurement");
        report.AppendLine();
        report.AppendLine("| Field | Value |");
        report.AppendLine("|---|---|");
        report.AppendLine(Invariant($"| Wall clock | {wallClock.TotalSeconds:F2} s |"));
        report.AppendLine(Invariant($"| Model time (all workers) | {modelTotal.TotalSeconds:F2} s |"));
        report.AppendLine(Invariant($"| Control-plane time (all workers) | {controlPlaneTotal.TotalSeconds:F2} s |"));
        report.AppendLine(Invariant($"| Control-plane per run | {controlPlaneTotal.TotalMilliseconds / runCount:F2} ms |"));
        report.AppendLine(Invariant($"| Control-plane per event | {controlPlaneTotal.TotalMilliseconds / eventCount:F2} ms |"));
        report.AppendLine();
        report.AppendLine("🚨 These numbers are a report, not a service level. They were measured on");
        report.AppendLine("the machine named above, against a containerised database on the same host,");
        report.AppendLine("with a simulated model. Model time and control-plane time are measured");
        report.AppendLine("separately and must not be added into a single \"runs per second\" figure.");

        return report.ToString();
    }

    private static async Task<string> WriteReportAsync(string report)
    {
        var directory = Path.Combine(RepoRoot.Path, "artifacts", "load");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            Invariant($"bounded-sql-load-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.md"));

        await File.WriteAllTextAsync(path, report);

        return path;
    }

    private const string NotAvailable = "not available on this platform";

    /// <summary>What the MACHINE is, as opposed to what this process was given.</summary>
    private sealed record MachineFacts(string? Processor, long? PhysicalMemoryBytes);

    private static string Describe(long? bytes)
        => bytes is { } value ? Invariant($"{value / (1024 * 1024)} MiB") : NotAvailable;

    /// <summary>
    /// Reads the real hardware behind the run, per platform. A report without
    /// these rows is still a report (K-738: it is not a gate), so every path
    /// here fails soft and the process-visible rows carry the run either way.
    /// </summary>
    private static async Task<MachineFacts> ReadMachineAsync()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var processor = await SysctlAsync("machdep.cpu.brand_string")
                    ?? await SysctlAsync("hw.model");

                return new MachineFacts(
                    processor,
                    long.TryParse(await SysctlAsync("hw.memsize"), CultureInfo.InvariantCulture, out var memory) ? memory : null);
            }

            if (OperatingSystem.IsLinux())
            {
                return new MachineFacts(ReadProcCpuModel(), ReadProcMemTotalBytes());
            }

            return new MachineFacts(null, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MachineFacts(null, null);
        }
    }

    /// <returns>The value of one <c>sysctl</c> key, or <see langword="null"/> when it cannot be read.</returns>
    private static async Task<string?> SysctlAsync(string key)
    {
        var result = await ProcessRunner.RunAsync(
            "sysctl",
            Invariant($"-n {key}"),
            RepoRoot.Path,
            TimeSpan.FromSeconds(10));

        if (result.ExitCode != 0)
        {
            return null;
        }

        var value = result.StandardOutput.Trim();

        return value.Length == 0 ? null : value;
    }

    /// <returns>The processor model from <c>/proc/cpuinfo</c>, or <see langword="null"/>.</returns>
    private static string? ReadProcCpuModel()
    {
        const string ProcCpuInfo = "/proc/cpuinfo";

        if (!File.Exists(ProcCpuInfo))
        {
            return null;
        }

        // "model name" on x86; arm64 kernels publish "Model name" or nothing at
        // all, in which case the row simply says so.
        foreach (var line in File.ReadLines(ProcCpuInfo))
        {
            if (!line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);

            if (separator >= 0 && line[(separator + 1)..].Trim() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <returns>Total machine memory from <c>/proc/meminfo</c>, or <see langword="null"/>.</returns>
    private static long? ReadProcMemTotalBytes()
    {
        const string ProcMemInfo = "/proc/meminfo";

        if (!File.Exists(ProcMemInfo))
        {
            return null;
        }

        foreach (var line in File.ReadLines(ProcMemInfo))
        {
            if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                continue;
            }

            // The line reads "MemTotal:       16316920 kB" - kibibytes, always.
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (fields.Length >= 2 && long.TryParse(fields[1], CultureInfo.InvariantCulture, out var kibibytes))
            {
                return kibibytes * 1024;
            }
        }

        return null;
    }

    private static async Task<string> ReadCommitAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                "git",
                "rev-parse HEAD",
                RepoRoot.Path,
                TimeSpan.FromSeconds(30));

            return result.ExitCode == 0 ? result.StandardOutput.Trim() : "unknown";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A report without a commit id is still a report; failing the run
            // over it would be the tail wagging the dog.
            return "unknown";
        }
    }

    private static string Invariant(FormattableString value) => FormattableString.Invariant(value);
}
