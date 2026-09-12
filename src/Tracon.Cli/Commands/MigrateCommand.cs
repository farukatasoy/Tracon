using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Cli.Commands;

/// <summary>Applies pending migrations directly against the database, without starting the application.</summary>
internal static class MigrateCommand
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
            var applier = services.GetRequiredService<IMigrationApplier>();

            try
            {
                var applied = await applier.ApplyAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"{applied} applied");
                return 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine("Canceled; the schema is left as it was before this run started (each migration commits in its own transaction).");
                return 2;
            }
            catch (DbException ex)
            {
                // ADO.NET provider exceptions (Npgsql/SqlClient/Sqlite) do not embed
                // the connection string, so ex.Message is safe to print (K-059).
                Console.Error.WriteLine($"Migration failed: {ex.GetType().Name}: {ex.Message}");
                return 2;
            }
        }
    }
}
