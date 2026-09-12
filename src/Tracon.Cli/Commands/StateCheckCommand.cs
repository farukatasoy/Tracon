using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Cli.Commands;

/// <summary>
/// Reports whether this build can read the state already in the database,
/// WITHOUT writing anything.
/// </summary>
/// <remarks>
/// <para>
/// It follows <see cref="MigrateCommand"/>'s shape rather than
/// <see cref="HealthCommand"/>'s: it talks to the database directly, because
/// the whole point is to ask the question BEFORE the upgrade, when the new
/// application may not be running at all.
/// </para>
/// <para>
/// Exit codes: <c>0</c> nothing found that blocks reading, <c>1</c> argument
/// error, <c>2</c> could not run, <c>3</c> ran and found unreadable state.
/// <c>3</c> matches <c>eval</c>'s "ran, but the answer is bad" code.
/// </para>
/// </remarks>
internal static class StateCheckCommand
{
    private const int DefaultSamplePerGeneration = 5;

    // camelCase to match every other JSON this product emits: the HTTP API's
    // responses and `eval --json`, whose generated types carry camelCase
    // [JsonPropertyName] attributes. A PascalCase document here would make one
    // command the odd one out in a `jq` pipeline.
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // A bare `0` for StatePreflightTarget tells a reader nothing and turns
        // a `jq` filter into a lookup against this source file. Every enum
        // Tracon serializes over the wire is a string; this one matches.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var provider = CliArgs.RequireOption(args, "--provider");
        var connectionString = CliArgs.RequireOption(args, "--connection", "TRACON_CONNECTION");
        var asJson = CliArgs.HasFlag(args, "--json");
        var sample = ReadSampleSize(args);

        // await using var x = ...; puts ConfigureAwait(false) out of reach for
        // the compiler-generated dispose call (MA0004); declaring the variable
        // first and wrapping usage in `await using (x.ConfigureAwait(false))`
        // keeps the concrete type usable. docs/hafiza/build-ve-analyzer.md.
        var services = SqlProviderSelector.BuildProvider(provider, connectionString);
        await using (services.ConfigureAwait(false))
        {
            var reader = services.GetRequiredService<IStatePreflightReader>();

            try
            {
                var report = await new StatePreflight(reader).RunAsync(sample, cancellationToken).ConfigureAwait(false);
                Print(report, asJson);

                return report.IsClean ? 0 : 3;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Nothing to unwind: this command only ever ran SELECTs.
                Console.Error.WriteLine("Canceled. Nothing was written; the check is read-only.");
                return 2;
            }
            catch (DbException ex)
            {
                // ADO.NET provider exceptions (Npgsql/SqlClient/Sqlite) do not embed
                // the connection string, so ex.Message is safe to print (K-059).
                Console.Error.WriteLine($"State check failed: {ex.GetType().Name}: {ex.Message}");
                return 2;
            }
            catch (TraconException ex)
            {
                // 🚨 A safety net, not the handler for a known case. The one
                // TraconException a preflight was ever expected to meet -
                // a row encrypted with a key this process does not hold - is
                // handled inside the reader and reported as a structure-only
                // row (K-735). This catch exists because an UNEXPECTED one
                // must still leave the operator a single line rather than a
                // stack trace; Tracon's own messages never carry the
                // connection string (K-059).
                Console.Error.WriteLine($"State check failed: {ex.Message}");
                return 2;
            }
        }
    }

    /// <summary>Reads <c>--sample</c>, defaulting when it is absent.</summary>
    /// <param name="args">The command arguments.</param>
    /// <returns>The number of rows to sample per generation.</returns>
    /// <exception cref="CliArgumentException">The value is not a non-negative integer.</exception>
    private static int ReadSampleSize(IReadOnlyList<string> args)
    {
        var raw = CliArgs.GetOption(args, "--sample");

        if (string.IsNullOrEmpty(raw))
        {
            return DefaultSamplePerGeneration;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sample) || sample < 0)
        {
            throw new CliArgumentException($"--sample must be a non-negative whole number; got '{raw}'.");
        }

        return sample;
    }

    /// <summary>Writes the report.</summary>
    /// <param name="report">The report to write.</param>
    /// <param name="asJson">Whether to emit the machine-readable document instead.</param>
    private static void Print(StatePreflightReport report, bool asJson)
    {
        if (asJson)
        {
            // 🚨 Under --json, stdout is ONE machine-readable document and
            // nothing else may join it; a second, human-shaped line there
            // turns `| jq` into a parse error.
            Console.WriteLine(JsonSerializer.Serialize(report, PrettyJson));

            return;
        }

        Console.WriteLine($"Provider: {report.ProviderName}");
        Console.WriteLine($"Microsoft Agent Framework in this build: {report.RunningMafVersion}");
        Console.WriteLine();

        PrintGenerations("Sessions", report.Sessions);
        PrintGenerations("Workflow checkpoints", report.Checkpoints);

        Console.WriteLine();

        // 🚨 The wording carries the whole meaning of this command. A sample
        // that came back clean says the rows that WERE read are readable; it
        // does NOT say every row is. An operator who reads "all readable"
        // here upgrades on evidence that was never collected.
        Console.WriteLine(
            $"Sampled {report.SampledCount} row(s), at most {report.SamplePerGeneration} per generation: " +
            $"{report.DecodedSampleCount} fully decoded, {report.StructureOnlySampleCount} checked for structure only, " +
            $"{report.SampleFailureCount} failed.");
        Console.WriteLine("This is a sample, not a survey: rows outside it were not read.");

        if (report.StructureOnlySampleCount > 0)
        {
            // 🚨 Name every reason, including the third one. An earlier version
            // listed only checkpoints and encryption, so a run whose
            // structure-only row was simply a FUTURE generation sent the
            // operator hunting for an encryption setting that was never on.
            Console.WriteLine(
                "A row is checked for structure only when it is a workflow checkpoint (no decoder exists "
                + "outside a running workflow), when it is encrypted at rest (this command holds no content "
                + "protection key), or when its generation is already reported above as unreadable.");
        }

        foreach (var failure in report.SampleFailures)
        {
            var recorded = failure.RecordedMafVersion ?? "unknown (written before version stamping existed)";

            Console.Error.WriteLine(
                $"  unreadable: {Describe(failure.Target)} '{failure.Id}' " +
                $"(generation {failure.SchemaGeneration?.ToString(CultureInfo.InvariantCulture) ?? "unstamped"}, " +
                $"written with Microsoft Agent Framework {recorded}): {failure.Reason}");
        }

        if (report.HasUnreadableGeneration)
        {
            Console.Error.WriteLine(
                $"{report.UnreadableRecordCount} row(s) carry a schema generation this build cannot read. " +
                "Upgrade the Tracon packages before starting this build against this database.");
        }

        Console.WriteLine();
        Console.WriteLine(report.IsClean
            ? "Result: nothing found that blocks this build from reading the stored state."
            : "Result: unreadable state found. Nothing was changed; see the lines above.");
    }

    /// <summary>Writes one table's generation counts.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="generations">The counts.</param>
    private static void PrintGenerations(string title, IReadOnlyList<StateGenerationCount> generations)
    {
        Console.WriteLine($"{title}:");

        if (generations.Count == 0)
        {
            Console.WriteLine("  (no rows)");

            return;
        }

        foreach (var generation in generations)
        {
            var name = generation.SchemaGeneration?.ToString(CultureInfo.InvariantCulture) ?? "unstamped";
            var verdict = generation.ReadableByThisBuild ? "readable by this build" : "NOT readable by this build";

            Console.WriteLine($"  generation {name}: {generation.RecordCount} row(s), {verdict}");
        }
    }

    /// <summary>Names a target the way an operator refers to it.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The label.</returns>
    private static string Describe(StatePreflightTarget target) =>
        target is StatePreflightTarget.Sessions ? "session" : "workflow checkpoint";
}
