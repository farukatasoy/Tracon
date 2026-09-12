using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Cli.Commands;

/// <summary>Lists pending migration names WITHOUT writing to the database.</summary>
internal static class MigrateStatusCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var provider = CliArgs.RequireOption(args, "--provider");
        var connectionString = CliArgs.RequireOption(args, "--connection", "TRACON_CONNECTION");

        // await using var x = ...; puts ConfigureAwait(false) out of reach for
        // the compiler-generated dispose call (MA0004); declaring the variable
        // first and wrapping usage in `await using (x.ConfigureAwait(false))`
        // keeps the concrete type usable. docs/hafiza/build-ve-analyzer.md.
        var services = SqlProviderSelector.BuildProvider(provider, connectionString);
        await using (services.ConfigureAwait(false))
        {
            var diagnostics = services.GetRequiredService<ISqlPersistenceDiagnostics>();

            try
            {
                var snapshot = await diagnostics.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

                if (!snapshot.CanConnect)
                {
                    Console.Error.WriteLine("Could not connect to the database.");
                    return 2;
                }

                if (snapshot.PendingMigrations.Count == 0)
                {
                    Console.WriteLine("0 pending");
                    return 0;
                }

                Console.WriteLine($"{snapshot.PendingMigrations.Count} pending");
                foreach (var name in snapshot.PendingMigrations)
                {
                    Console.WriteLine(name);
                }

                return 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine("Canceled.");
                return 2;
            }
            catch (DbException ex)
            {
                // ADO.NET provider exceptions (Npgsql/SqlClient/Sqlite) do not embed
                // the connection string, so ex.Message is safe to print (K-059).
                Console.Error.WriteLine($"Status check failed: {ex.GetType().Name}: {ex.Message}");
                return 2;
            }
        }
    }
}
