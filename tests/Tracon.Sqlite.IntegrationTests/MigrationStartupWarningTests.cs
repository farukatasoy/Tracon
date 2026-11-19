using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A host whose migrations are not applied at start (<c>AutoApplyMigrations = false</c>) says so when the schema is
/// not there, instead of starting silently and failing at the first run.
/// </summary>
/// <remarks>
/// <para>
/// Runs on a real SQLite file because the behavior crosses the store boundary: the warning comes from reading the
/// migration ledger of the database the host is configured for, and a fake ledger would prove only the fake.
/// </para>
/// <para>
/// The host still starts in every case. A deployment that applies the schema in its own step must not be stopped by
/// a database that is slow to answer; the warning is what tells the operator that the step did not run.
/// </para>
/// </remarks>
public sealed class MigrationStartupWarningTests(SqliteFixture fixture)
{
    private const string NotAppliedFragment = "Tracon migrations are not applied automatically";

    [Fact]
    public async Task A_schema_that_was_never_applied_is_warned_about_and_the_host_still_starts()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        using var logs = new ListLoggerProvider();

        await using var provider = Build(fixture.ConnectionStringFor(prefix), prefix, logs);

        await StartMigrationServiceAsync(provider);

        var warning = logs.Entries.Single(static entry => entry.Level == LogLevel.Warning);

        warning.Message.ShouldContain(NotAppliedFragment);
        warning.Message.ShouldContain("pending");
        warning.Message.ShouldContain("0001", Case.Insensitive, "the message names the first migration that is missing");
        warning.Message.ShouldContain("tracon migrate");

        // The warning does not hold the host back: background services still start.
        provider.GetRequiredService<SchemaReadyGate>().IsReady.ShouldBeTrue();
    }

    [Fact]
    public async Task A_schema_that_is_current_stays_at_information()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        var connectionString = fixture.ConnectionStringFor(prefix);

        // The deployment step the host leaves to the operator: the schema is applied before the host starts. The
        // context drops its tables when it is disposed, so it lives as long as the host does.
        await using var context = SqliteTestContext.Create(fixture, prefix);
        await context.Migrations.ApplyAsync(TestContext.Current.CancellationToken);

        using var logs = new ListLoggerProvider();

        await using var provider = Build(connectionString, prefix, logs);

        await StartMigrationServiceAsync(provider);

        logs.Entries.Where(static entry => entry.Level >= LogLevel.Warning).Select(static entry => entry.Message).ShouldBeEmpty();
        logs.Entries.ShouldContain(static entry => entry.Level == LogLevel.Information && entry.Message.Contains(NotAppliedFragment, StringComparison.Ordinal));
        provider.GetRequiredService<SchemaReadyGate>().IsReady.ShouldBeTrue();
    }

    [Fact]
    public async Task A_database_that_cannot_be_reached_is_warned_about_and_the_host_still_starts()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"tracon-missing-{Guid.NewGuid():N}", "db.sqlite");
        using var logs = new ListLoggerProvider();

        await using var provider = Build($"Data Source={missingDirectory}", prefix, logs);

        await StartMigrationServiceAsync(provider);

        var warning = logs.Entries.Single(static entry => entry.Level == LogLevel.Warning);

        warning.Message.ShouldContain(NotAppliedFragment);
        warning.Message.ShouldContain("could not be reached");
        warning.Message.ShouldContain("not verified");
        provider.GetRequiredService<SchemaReadyGate>().IsReady.ShouldBeTrue();
    }

    private static ServiceProvider Build(string connectionString, string tablePrefix, ListLoggerProvider logs)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<ILoggerProvider>(logs);
        services.AddTracon().UseSqlite(options =>
        {
            options.ConnectionString = connectionString;
            options.TablePrefix = tablePrefix;
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }

    private static async Task StartMigrationServiceAsync(ServiceProvider provider)
    {
        var service = provider.GetServices<IHostedService>().OfType<MigrationHostedService>().Single();

        await service.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Collects the log entries of the container that holds it.</summary>
    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new ListLogger(_entries);

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class ListLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
