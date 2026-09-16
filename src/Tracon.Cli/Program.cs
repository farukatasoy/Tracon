using Tracon.Cli.Commands;

namespace Tracon.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return args[0] switch
            {
                "migrate" when args.Length > 1 && string.Equals(args[1], "status", StringComparison.Ordinal) =>
                    await MigrateStatusCommand.RunAsync(args[2..], cancellation.Token).ConfigureAwait(false),
                "migrate" =>
                    await MigrateCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
                "state-check" =>
                    await StateCheckCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
                "health" =>
                    await HealthCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
                "eval" =>
                    await EvalCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
                "agent-skill" =>
                    await AgentSkillCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
                _ => PrintUnknownCommand(args[0]),
            };
        }
        catch (CliArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Run 'tracon --help' for usage.");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            tracon - Tracon management CLI

            Usage:
              tracon migrate --provider <postgres|sqlserver|sqlite> --connection <connection-string>
              tracon migrate status --provider <postgres|sqlserver|sqlite> --connection <connection-string>
              tracon state-check --provider <postgres|sqlserver|sqlite> --connection <connection-string>
                                     [--sample <n>] [--json]
              tracon health --url <base-url> [--token <token>] [--json]
              tracon agent-skill [--format claude] [--output <directory>]
                              [--force] [--json]
              tracon eval --url <base-url> --suite <name>
                              [--token <token>] [--agent-version <n>]
                              [--min-pass-rate <0..1>] [--max-failures <n>]
                              [--baseline <runId|previous>] [--max-regressions <n>]
                              [--timeout <seconds>] [--poll-interval <seconds>]
                              [--json]

            The connection string and the bearer token can also come from the
            TRACON_CONNECTION and TRACON_TOKEN environment variables
            instead of --connection/--token. Neither is ever read from a
            configuration file.

            --url is the application root PLUS the MapTracon prefix, for
            example http://localhost:5080/tracon for the default prefix.

            state-check answers "can this build read the state already in the
            database" BEFORE the upgrade, without starting the application and
            without writing anything - no row, no migration, no lock. It counts
            the stored schema generations across every tenant and compares them
            against what this build writes, then tries to decode at most
            --sample rows of EACH generation (default 5; 0 counts only).

            The count covers every row. The decode is a SAMPLE: a run with no
            failures says the rows that were read came back readable, never
            that all of them would. Exit codes: 0 = nothing found that blocks
            reading, 1 = argument error, 2 = could not run, 3 = ran and found
            state this build cannot read.

            agent-skill writes the Tracon gate skill into a repository, so the
            coding agent working there reads the capability map of the
            installed version before it writes code Tracon already ships. It
            writes ONE file, .claude/skills/tracon/SKILL.md, under --output
            (default: the root of the repository you run it in, which is where
            the build looks for it). It prints the path it wrote.

            An existing file is never touched without --force, because it may
            carry your own edits; the command says what it did and exits 0
            either way. The file carries the capability map revision it was
            written from, and the build warns with TRC0403 once the installed
            packages ship a newer one.

            Exit codes: 0 = written, or left alone, 1 = argument error,
            2 = could not write.

            eval triggers a suite, polls it to completion (default timeout 30
            minutes, poll interval 5 seconds), and applies an optional quality
            gate. With neither --min-pass-rate nor --max-failures, there is no
            absolute gate: exit 0 once the run finishes. With one or both
            given, ALL given thresholds must hold or the command exits 3.

            --baseline adds a RELATIVE gate on top, which is what catches a
            slide an absolute threshold cannot see: with --min-pass-rate 0.85
            set, a drop from 95% to 90% still passes. It names an earlier run
            of the same suite, either by id or as the word 'previous' (the
            newest completed run before this one), and --max-regressions says
            how many cases may break against it. --baseline WITHOUT
            --max-regressions reports the comparison and never fails the build;
            the ceiling is what turns a report into a gate. Cases added to or dropped
            from the suite are never counted as regressions. --max-regressions
            without --baseline is an argument error, never a silent no-op. On
            a suite's first run 'previous' finds nothing, says so on stderr,
            and does NOT fail the gate.

            Exit codes: 0 = ran and passed the gate (or no gate given),
            1 = argument error, 2 = could not run (transport, server, timeout,
            or the eval itself ended Failed/Cancelled), 3 = ran but missed the
            gate, 4 = ran, but the comparison against the baseline was
            impossible (its per-case results are gone, it never completed, or
            it measures another suite). 4 is deliberately not 3: a lost
            history needs a different fix than a broken case.
            """);
    }
}
