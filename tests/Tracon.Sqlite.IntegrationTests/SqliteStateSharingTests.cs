using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Whether the state of a SQLite provider is still there after a restart follows the connection string: a file is, a
/// shared in-memory database is not.
/// </summary>
/// <remarks>
/// The production profile asks whether a run that a restart cut off stays <c>Running</c>. A shared in-memory database is
/// a supported setup and dies with the process, so a host on one has nothing to decide.
/// </remarks>
public sealed class SqliteStateSharingTests
{
    [Theory]
    [InlineData("Data Source=file:tracon?mode=memory&cache=shared")]
    [InlineData("Data Source=file:tracon?cache=shared&mode=memory")]
    [InlineData("Data Source=file:tracon?MODE=Memory&cache=shared")]
    [InlineData("Data Source=file::memory:?cache=shared")]
    [InlineData("Data Source=file:/tracon?vfs=memdb")]
    [InlineData("Data Source=tracon;Mode=Memory;Cache=Shared")]
    [InlineData("Data Source=:memory:")]
    public void A_database_that_lives_in_memory_does_not_outlive_the_process(string connectionString)
    {
        var sharing = SqliteStateSharing.For(new TraconSqliteOptions { ConnectionString = connectionString });

        sharing.OutlivesProcess.ShouldBeFalse();
        sharing.AcrossProcesses.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Data Source=tracon.db")]
    [InlineData("Data Source=/var/lib/tracon/tracon.db;Cache=Shared")]
    [InlineData("Data Source=file:tracon.db?cache=shared")]
    [InlineData("Data Source=file:/var/lib/memory/tracon.db")]
    [InlineData("Data Source=file:tracon.db?mode=rwc")]
    [InlineData("Data Source=tracon.db;Mode=ReadWriteCreate")]
    public void A_file_outlives_the_process(string connectionString)
    {
        var sharing = SqliteStateSharing.For(new TraconSqliteOptions { ConnectionString = connectionString });

        sharing.OutlivesProcess.ShouldBeTrue();
        sharing.AcrossProcesses.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a connection string")]
    public void A_connection_string_that_names_nothing_is_taken_for_a_file_so_the_question_is_asked(string? connectionString)
    {
        SqliteStateSharing.For(new TraconSqliteOptions { ConnectionString = connectionString }).OutlivesProcess.ShouldBeTrue();
    }

    [Fact]
    public void UseSqlite_registers_the_value_for_the_connection_string_the_host_bound()
    {
        using var memory = Build("Data Source=file:state-sharing-memory?mode=memory&cache=shared");
        using var file = Build("Data Source=state-sharing-file.db");

        memory.GetRequiredService<StateSharing>().OutlivesProcess.ShouldBeFalse();
        file.GetRequiredService<StateSharing>().OutlivesProcess.ShouldBeTrue();
    }

    private static ServiceProvider Build(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddTracon().UseSqlite(options =>
        {
            options.ConnectionString = connectionString;
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }
}
