using AgentPrism.Cli.Commands;

namespace AgentPrism.Cli;

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
                "health" =>
                    await HealthCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
                "eval" =>
                    await EvalCommand.RunAsync(args[1..], cancellation.Token).ConfigureAwait(false),
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
        Console.Error.WriteLine($"Unknown command '{command}'. Run 'agentprism --help' for usage.");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            agentprism - AgentPrism management CLI

            Usage:
              agentprism migrate --provider <postgres|sqlserver|sqlite> --connection <connection-string>
              agentprism migrate status --provider <postgres|sqlserver|sqlite> --connection <connection-string>
              agentprism health --url <base-url> [--token <token>] [--json]
              agentprism eval --url <base-url> --suite <name>
                              [--token <token>] [--agent-version <n>]
                              [--min-pass-rate <0..1>] [--max-failures <n>]
                              [--timeout <seconds>] [--poll-interval <seconds>]
                              [--json]

            The connection string and the bearer token can also come from the
            AGENTPRISM_CONNECTION and AGENTPRISM_TOKEN environment variables
            instead of --connection/--token. Neither is ever read from a
            configuration file.

            --url is the application root PLUS the MapAgentPrism prefix, for
            example http://localhost:5080/agentprism for the default prefix.

            eval triggers a suite, polls it to completion (default timeout 30
            minutes, poll interval 5 seconds), and applies an optional quality
            gate. With neither --min-pass-rate nor --max-failures, there is no
            gate: exit 0 once the run finishes. With one or both given, ALL
            given thresholds must hold or the command exits 3. Exit codes:
            0 = ran and passed the gate (or no gate given), 1 = argument
            error, 2 = could not run (transport, server, timeout, or the eval
            itself ended Failed/Cancelled), 3 = ran but missed the gate.
            """);
    }
}
