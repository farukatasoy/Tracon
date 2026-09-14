// The Tracon capacity host (Phase 166).
//
// A real ASP.NET Core application that consumes Tracon the way a customer
// does: one exact PackageReference, restored from an isolated feed with an
// empty cache. Nothing here references src/.
//
// Three modes, selected by TRACON_CAPACITY_MODE:
//   api    -> MapTracon + its own in-process worker setting (from the profile)
//   worker -> Scheduling.RunWorker = true, NO HTTP surface; the worker axis
//   seed   -> writes the "full database" fixture through the public store, exits
//
// 🚨 One project, not two. A second host project would be a synchronization
// copy of the same registration, and the two would drift silently (K-411).
//
// The process prints CAPACITY-HOST-READY (or CAPACITY-WORKER-READY) once it is
// listening. The orchestrator waits for that LINE, not for a duration.
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Tracon;
using Tracon.Capacity;
using Tracon.CapacityHost;

var settings = CapacitySettings.FromEnvironment();

if (string.Equals(settings.Mode, CapacityContract.MigrateMode, StringComparison.Ordinal))
{
    return await MigrateAsync(settings).ConfigureAwait(false);
}

if (string.Equals(settings.Mode, CapacityContract.SeedMode, StringComparison.Ordinal))
{
    return await SeedAsync(settings).ConfigureAwait(false);
}

var isWorker = string.Equals(settings.Mode, CapacityContract.WorkerMode, StringComparison.Ordinal);

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

// 🚨 Warning, not Information. Console logging at Information turns the
// measurement into a measurement of stdout: it is synchronous, it serialises
// under load, and the driver's redirected pipe would become the bottleneck.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

if (!isWorker)
{
    builder.WebHost.UseUrls(string.Create(
        CultureInfo.InvariantCulture,
        $"http://127.0.0.1:{settings.Port}"));
}

var counters = new CapacityCounters();
using var executions = new CapacityExecutionLog(settings.ExecutionLogDirectory, settings.Name);
var probe = new CapacityProbeTool(settings.Workload, counters);

builder.Services.AddSingleton(counters);
builder.Services.AddSingleton(settings);

var tracon = builder.Services.AddTracon();

tracon
    .AddModelProvider(new CapacityModelProvider(settings.Workload, counters, executions, settings.Name))
    .AddTool(probe.CreateFunction())
    .AddAgent(new AgentDefinition
    {
        Name = CapacityContract.AgentName,
        DisplayName = "Capacity fixture agent",
        Description = "The agent under load. Its answer is deterministic and carries the request's correlation value.",
        Instructions = "Call the probe tool with the correlation value from the message, then answer.",
        Model = new ModelBinding
        {
            Provider = CapacityContract.ProviderName,
            Model = CapacityContract.ModelName,
        },
        ToolNames = [CapacityPayload.ToolName],
        Origin = AgentDefinitionOrigin.Code,
    })
    .UsePostgreSql(options =>
    {
        options.ConnectionString = ConnectionStringWithPool(settings);
        options.SchemaName = settings.Schema;

        // The orchestrator migrates the schema once, before any process starts.
        // Migrating here would put schema creation inside what the first cell
        // measures, and every worker process would race for the same lock.
        options.AutoApplyMigrations = false;
    });

// 🚨 The tenant header is resolved ONLY here, at the loopback fixture
// boundary. This is not a production authentication shape and the report says
// so: nothing measured here describes what real identity resolution costs.
tracon.UseTenancy(options =>
{
    options.Enabled = true;
    options.AllowHeaderResolution = true;
    options.HeaderName = CapacityContract.TenantHeader;
    options.AllowedTenants.Add(CapacityContract.TenantA);
    options.AllowedTenants.Add(CapacityContract.TenantB);
});

builder.Services.UseScheduling(options =>
{
    options.Enabled = true;

    // 🚨 In the worker profile this is FALSE on the API host: otherwise the
    // "1 worker" cell is really two workers and the whole axis shifts. The
    // effective value is served from /capacity/settings so the driver can
    // check it at run time rather than trust this line.
    options.RunWorker = isWorker || !CapacityEnvironment.WorkerAxisActive;
    options.PollInterval = TimeSpan.FromMilliseconds(100);
    options.LeaseDuration = TimeSpan.FromMinutes(2);
    options.MaxConcurrentJobs = CapacityEnvironment.MaxConcurrentJobs;
});

var app = builder.Build();

if (!isWorker)
{
    app.MapTracon(CapacityContract.Prefix);
    CapacityApparatus.Map(app, settings, counters);
}

await app.StartAsync().ConfigureAwait(false);

Console.WriteLine(isWorker ? CapacityContract.WorkerReadyLine : CapacityContract.HostReadyLine);
Console.Out.Flush();

await app.WaitForShutdownAsync().ConfigureAwait(false);
return 0;

static string ConnectionStringWithPool(CapacitySettings settings)
{
    // The pool ceiling is set EXPLICITLY and reported, rather than inherited
    // from a default nobody wrote down. A different value is a different
    // configuration and a different measurement.
    var parsed = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
    {
        MaxPoolSize = settings.MaxPoolSize,
    };

    return parsed.ConnectionString;
}

static async Task<int> MigrateAsync(CapacitySettings settings)
{
    using var built = BuildBackgroundHost(settings, autoApplyMigrations: true);
    await built.StartAsync().ConfigureAwait(false);

    Console.WriteLine(CapacityContract.MigratedLine);
    Console.Out.Flush();

    await built.StopAsync().ConfigureAwait(false);
    return 0;
}

static IHost BuildBackgroundHost(CapacitySettings settings, bool autoApplyMigrations)
{
    var host = Host.CreateApplicationBuilder();
    host.Logging.ClearProviders();

    host.Services.AddTracon()
        .AddModelProvider(new CapacityModelProvider(
            settings.Workload,
            new CapacityCounters(),
            new CapacityExecutionLog(directory: null, settings.Name),
            settings.Name))
        .UsePostgreSql(options =>
        {
            options.ConnectionString = settings.ConnectionString;
            options.SchemaName = settings.Schema;
            options.AutoApplyMigrations = autoApplyMigrations;
        });

    // Neither background mode leases: migrating and seeding must not compete
    // with anything, and a worker started here would begin draining a queue
    // the measurement has not opened yet.
    host.Services.UseScheduling(options => options.RunWorker = false);

    return host.Build();
}

static async Task<int> SeedAsync(CapacitySettings settings)
{
    using var built = BuildBackgroundHost(settings, autoApplyMigrations: false);
    await built.StartAsync().ConfigureAwait(false);

    var runs = built.Services.GetRequiredService<IRunStore>();
    var (writtenRuns, writtenEvents) = await CapacitySeeder
        .SeedAsync(runs, settings.SeedRuns, CancellationToken.None)
        .ConfigureAwait(false);

    // The orchestrator verifies these against the real row counts; a seed that
    // claims more than it wrote would make the "full database" cell a fiction.
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"CAPACITY-SEEDED runs={writtenRuns} events={writtenEvents}"));

    await built.StopAsync().ConfigureAwait(false);
    return 0;
}
