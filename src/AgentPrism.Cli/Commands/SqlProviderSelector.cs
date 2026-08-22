using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Cli.Commands;

/// <summary>Wires <c>--provider</c> to the matching <c>Use*</c> extension. Shared by <see cref="MigrateCommand"/> and <see cref="MigrateStatusCommand"/>.</summary>
internal static class SqlProviderSelector
{
    public static void Register(IAgentPrismBuilder builder, string provider, string connectionString)
    {
        switch (provider.Trim().ToLowerInvariant())
        {
            case "postgres":
            case "postgresql":
                builder.UsePostgreSql(connectionString);
                break;
            case "sqlserver":
                builder.UseSqlServer(connectionString);
                break;
            case "sqlite":
                builder.UseSqlite(connectionString);
                break;
            default:
                throw new CliArgumentException(
                    $"Unknown provider '{provider}'. Use one of: postgres, sqlserver, sqlite.");
        }
    }

    /// <summary>
    /// Builds a minimal service provider with the selected SQL provider
    /// registered, ready to resolve <see cref="IMigrationApplier"/> or
    /// <see cref="ISqlPersistenceDiagnostics"/> from (not the concrete
    /// <c>MigrationRunner</c> type - it is compiled once per SQL provider
    /// package from shared source, so an unqualified reference to it is
    /// ambiguous the moment more than one provider assembly is loaded, as
    /// this project's does).
    /// </summary>
    public static ServiceProvider BuildProvider(string provider, string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddAgentPrism();
        Register(builder, provider, connectionString);

        return services.BuildServiceProvider();
    }
}
