using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// The Production installation says, on the server side, that its storage does
/// not survive a restart. The console already showed this; the log did not.
/// </summary>
/// <remarks>
/// These run at the functional level on purpose: the behavior crosses the DI
/// and hosting boundary (which store the container resolved, which environment
/// the host reports), and a unit test over the service alone cannot prove that
/// the registration reaches a started host.
/// </remarks>
public sealed class NonPersistentStorageWarningTests
{
    private const string WarningFragment = "storage that is not persistent";

    [Fact]
    public async Task Production_with_the_default_stores_warns_exactly_once()
    {
        await using var host = await AgentPrismTestHost.StartAsync(environment: Environments.Production);

        var matches = host.Logs.Entries
            .Where(entry => entry.Contains(WarningFragment, StringComparison.Ordinal))
            .ToList();

        matches.Count.ShouldBe(1);

        // The level is a recorded decision, not a detail: a supported mode does
        // not produce Critical. RecordingLoggerProvider writes "<level> <category> …".
        matches[0].ShouldStartWith($"{LogLevel.Warning} {nameof(AgentPrism)}.{nameof(NonPersistentStorageWarningService)}");
        matches[0].ShouldContain("agent definitions, runs, sessions");
        matches[0].ShouldContain("UsePostgreSql");
    }

    [Fact]
    public async Task Development_with_the_default_stores_stays_quiet()
    {
        await using var host = await AgentPrismTestHost.StartAsync(environment: Environments.Development);

        host.Logs.AllText.ShouldNotContain(WarningFragment);
    }

    [Fact]
    public async Task A_persistent_provider_stays_quiet_in_Production()
    {
        // A file database, not `:memory:`: without a shared cache every connection
        // would open its own empty database (see A2AEndpointTests).
        var databasePath = Path.Combine(Path.GetTempPath(), $"agentprism-persist-warn-{Guid.NewGuid():N}.db");

        try
        {
            await using var host = await AgentPrismTestHost.StartAsync(
                configureAgentPrism: builder => builder.UseSqlite($"Data Source={databasePath}"),
                environment: Environments.Production);

            host.Logs.AllText.ShouldNotContain(WarningFragment);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task The_warning_and_the_meta_endpoint_report_the_same_judgement()
    {
        await using var host = await AgentPrismTestHost.StartAsync(environment: Environments.Production);

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/meta", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("storage").GetProperty("persistent").GetBoolean().ShouldBeFalse();
        host.Logs.AllText.ShouldContain(WarningFragment);
    }

    [Fact]
    public async Task An_unreachable_database_does_not_change_the_judgement()
    {
        // The check opens no connection and reads no schema: it only looks at
        // which store implementation the container resolved. Migrations are off
        // here, so nothing else touches the database either - a database that
        // could never be opened still counts as persistent storage, and the host
        // starts.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseSqlite(options =>
            {
                options.ConnectionString = "Data Source=/nonexistent-directory/agentprism.db";
                options.AutoApplyMigrations = false;
            }),
            environment: Environments.Production);

        host.Logs.AllText.ShouldNotContain(WarningFragment);
    }
}
