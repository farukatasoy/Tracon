using Microsoft.Extensions.DependencyInjection;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// No factory that <c>AddTracon()</c> or a SQL provider registers returns <see langword="null"/>.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft's container accepts a factory that returns <see langword="null"/>: <c>GetService</c>
/// hands the null back, and <c>GetRequiredService</c> fails only for that one service. Autofac,
/// which ABP and many other hosts plug in through <c>UseServiceProviderFactory</c>, throws "A
/// delegate registered to create instances of ... returned null" instead, and so does any
/// constructor that takes the service. A consumer measured it on an ABP host:
/// <c>UsePostgreSql</c> registered <c>IVectorSearchStore</c> with a factory that returned null
/// while knowledge was off, and the first <c>IAgentCatalog</c> resolution failed on the
/// compiler's dependency on it. Every test host here uses Microsoft's container, so none of them
/// could see it.
/// </para>
/// <para>
/// The check calls every registered factory once, under the container the descriptors were built
/// into, and refuses a null result. It does not need a third-party container: a non-null result
/// is what every container requires, and a factory is the only registration form that can
/// produce a null. No factory opens a connection: the connection strings point at port 1 and at
/// a SQLite file nobody creates, the same way <see cref="SqlProviderRegistrationParityTests"/>
/// sets them.
/// </para>
/// </remarks>
public sealed class RegistrationFactoryNullTests
{
    private static readonly string SqliteDatabasePath = Path.Combine(
        Path.GetTempPath(),
        $"tracon-factory-null-{Guid.NewGuid():N}.db");

    public static TheoryData<string> Chains => ["AddTracon", "PostgreSQL", "SQL Server", "SQLite"];

    /// <summary>Every factory in the chain returns an instance.</summary>
    [Theory]
    [MemberData(nameof(Chains))]
    public async Task No_registered_factory_returns_null(string chain)
    {
        var services = new ServiceCollection();
        Use(chain, services.AddTracon());

        var nulls = new List<string>();

        await using (var provider = services.BuildServiceProvider())
        {
            await using var scope = provider.CreateAsyncScope();

            foreach (var descriptor in services)
            {
                var instance = descriptor switch
                {
                    { IsKeyedService: false, ImplementationFactory: { } factory } => factory(scope.ServiceProvider),
                    { IsKeyedService: true, KeyedImplementationFactory: { } keyed } =>
                        keyed(scope.ServiceProvider, descriptor.ServiceKey),
                    _ => NotAFactory,
                };

                if (instance is null)
                {
                    nulls.Add(descriptor.IsKeyedService
                        ? $"{descriptor.ServiceType.FullName} (key {descriptor.ServiceKey})"
                        : descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name);
                }
            }
        }

        File.Exists(SqliteDatabasePath).ShouldBeFalse("Calling the factories must not create the SQLite database.");

        nulls.ShouldBeEmpty(
            customMessage: $"With {chain}, these factories return null. Autofac and other containers refuse a " +
                           "null from a factory, so the host fails on the first resolution that needs the " +
                           "service. Register nothing when the service is unavailable, and read availability " +
                           $"through a non-null holder instead:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, nulls));
    }

    private static readonly object NotAFactory = new();

    private static void Use(string chain, ITraconBuilder builder)
    {
        switch (chain)
        {
            case "AddTracon":
                break;
            case "PostgreSQL":
                builder.UsePostgreSql(static options =>
                {
                    options.ConnectionString = "Host=localhost;Port=1;Database=unused;Username=unused;Timeout=1";
                    options.AutoApplyMigrations = false;
                });
                break;
            case "SQL Server":
                builder.UseSqlServer(static options =>
                {
                    options.ConnectionString =
                        "Server=localhost,1;Database=unused;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=1";
                    options.AutoApplyMigrations = false;
                });
                break;
            case "SQLite":
                builder.UseSqlite(static options =>
                {
                    options.ConnectionString = $"Data Source={SqliteDatabasePath}";
                    options.AutoApplyMigrations = false;
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain.");
        }
    }
}
