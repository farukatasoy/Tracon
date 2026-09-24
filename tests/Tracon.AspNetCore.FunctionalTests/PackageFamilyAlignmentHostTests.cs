using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The package family alignment check through a REAL host start
/// (<c>WebApplication.StartAsync</c>): <c>AddTracon()</c> registers it, and a
/// refusal stops the host before any hosted service starts - including one the
/// application registered ahead of <c>AddTracon()</c> and a storage provider's
/// migration, whatever <see cref="HostOptions.ServicesStartConcurrently"/> says.
/// </summary>
/// <remarks>
/// The mixed graph is simulated through the one seam the check has: a
/// <see cref="LoadedPackageFamily"/> registered before <c>AddTracon()</c>, which
/// its <c>TryAdd</c> keeps. The check itself is the one <c>AddTracon()</c>
/// registered; nothing here adds it. The real reading of a mixed graph is proven
/// against packed packages in <c>Tracon.Package.Tests</c>.
/// </remarks>
public sealed class PackageFamilyAlignmentHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_mixed_family_stops_the_host_before_any_hosted_service_starts(bool startConcurrently)
    {
        var recorder = new StartRecorder();
        using var database = new TempSqliteDatabase("family-alignment");

        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await TraconTestHost.StartAsync(
                configureTracon: tracon => tracon.UseSqlite(database.ConnectionString),
                configureServices: services =>
                {
                    // Both registered BEFORE AddTracon(), the order a host's own
                    // Program.cs produces: the recorder would start first.
                    services.Configure<HostOptions>(options => options.ServicesStartConcurrently = startConcurrently);
                    services.AddSingleton(recorder);
                    services.AddHostedService<RecordingHostedService>();
                    services.AddSingleton<LoadedPackageFamily>(new FixedFamily(
                        new LoadedFamilyAssembly("Tracon.AspNetCore", "1.0.0-preview.2"),
                        new LoadedFamilyAssembly("Tracon.Core", "1.0.0-preview.3")));
                }));

        exception.Message.ShouldContain("    Tracon.AspNetCore 1.0.0-preview.2");
        exception.Message.ShouldContain("    Tracon.Core 1.0.0-preview.3");
        recorder.Started.ShouldBeFalse("A hosted service registered before AddTracon() started on a mixed graph.");
        CountTables(database).ShouldBe(0, "The SQLite migration ran on a mixed graph.");
    }

    [Fact]
    public async Task An_aligned_family_starts_the_host_and_the_check_is_registered_by_AddTracon()
    {
        var recorder = new StartRecorder();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton(recorder);
                services.AddHostedService<RecordingHostedService>();
            });

        recorder.Started.ShouldBeTrue();
        host.Services.GetServices<IHostedService>().OfType<PackageFamilyAlignmentService>().Count().ShouldBe(1);
        host.Services.GetRequiredService<LoadedPackageFamily>().GetType().ShouldBe(typeof(LoadedPackageFamily));
    }

    private static int CountTables(TempSqliteDatabase database)
    {
        if (!File.Exists(database.Path))
        {
            return 0;
        }

        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";

        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class StartRecorder
    {
        private int _started;

        public bool Started => Volatile.Read(ref _started) == 1;

        public void MarkStarted() => Volatile.Write(ref _started, 1);
    }

    private sealed class RecordingHostedService(StartRecorder recorder) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            recorder.MarkStarted();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedFamily(params LoadedFamilyAssembly[] assemblies) : LoadedPackageFamily
    {
        internal override IReadOnlyList<LoadedFamilyAssembly> Read() => assemblies;
    }
}
