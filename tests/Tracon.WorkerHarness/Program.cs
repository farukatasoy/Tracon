// The Tracon worker harness (Phase 157).
//
// A real, separate process that registers Tracon the way a production
// worker node does, so the two-process failure proof kills a real host rather
// than an in-process fake. It is deliberately thin: one SQL store, one job
// handler, no model provider and no HTTP surface. What it proves is the
// scheduling boundary, and anything else in the process would only add
// startup time to a test that measures a lease clock.
//
// Two modes, the two shapes docs-site/guides/production.md describes:
//   worker -> Scheduling.RunWorker = true  (a worker node)
//   api    -> Scheduling.RunWorker = false (an API node: same stores, no leasing)
//
// It prints HARNESS-READY once the host is running. The test waits for that
// LINE, not for a duration - a fixed sleep is what makes a process test flaky
// on a loaded agent.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon;
using Tracon.Tests.Common;
using Tracon.WorkerHarness;

var settings = WorkerHarnessSettings.FromEnvironment();

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.Configure<WorkerHarnessSettingsHolder>(holder => holder.Settings = settings);

var tracon = builder.Services.AddTracon();

// The test owns the schema: it migrates once, then starts both processes
// against it. Migrating here as well would make the harness's startup time
// part of what the lease clock measures, and K-354's gate still opens on this
// path - MigrationHostedService calls MarkReady even when auto-apply is off.
if (settings.Provider == WorkerHarnessProvider.SqlServer)
{
    tracon.UseSqlServer(options =>
    {
        options.ConnectionString = settings.ConnectionString;
        options.SchemaName = settings.SchemaName;
        options.AutoApplyMigrations = false;
    });
}
else
{
    tracon.UsePostgreSql(options =>
    {
        options.ConnectionString = settings.ConnectionString;
        options.SchemaName = settings.SchemaName;
        options.AutoApplyMigrations = false;
    });
}

tracon.AddJobHandler<LongRunningJobHandler>(WorkerHarnessContract.HandlerKey);

// Phase 197: the two-step workflow a test kills in its second step. The first
// step returns at once, so its checkpoint exists before the kill; the second
// takes the work duration. Both write to the shared execution log with the
// run they ran in, so the test can tell a repeated step from a resumed one.
if (settings.Workflow)
{
    HarnessWorkflow.Register(tracon, settings);
}

tracon.UseScheduling(options =>
{
    options.Enabled = true;
    options.RunWorker = settings.Mode == WorkerHarnessMode.Worker;
    options.LeaseDuration = settings.LeaseDuration;
    options.PollInterval = settings.PollInterval;
    options.MaxAttempts = settings.MaxAttempts;
    options.MaxConcurrentJobs = 1;
});

var host = builder.Build();

await host.StartAsync();

Console.WriteLine(WorkerHarnessContract.ReadyLine);
Console.Out.Flush();

await host.WaitForShutdownAsync();
