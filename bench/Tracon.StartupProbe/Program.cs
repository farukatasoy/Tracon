using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon;

// Measures one cold process: what a host pays to start, what its first request pays, and how often it wakes while idle.
// Every run is a fresh process on purpose - start-up cost is mostly JIT, and a second measurement in the same process
// would see methods that are already compiled. Run each line three times and write down the range with the machine.
//
//   dotnet run -c Release --project bench/Tracon.StartupProbe -- cold bare
//   dotnet run -c Release --project bench/Tracon.StartupProbe -- first-request web
//   dotnet run -c Release --project bench/Tracon.StartupProbe -- idle web
//   dotnet run -c Release --project bench/Tracon.StartupProbe -- compose bare
//
// None of these is a gate: start-up time, first-request cost and idle wake-ups depend on the machine, and the allocation
// of composing a host differs by a few hundred bytes between processes under BenchmarkDotNet (four runs of the same code
// gave 336,235 to 336,681 B), which a gate that allows no growth cannot hold. What stops the registrations from growing
// unseen is the exact list of descriptors a test pins and the closed list of hosted services.
//
// Setups: none (empty generic host) · bare (AddTracon) · full (AddTracon + workflows + MCP + SQLite) ·
// web-none (web host with one endpoint) · web (web host + AddTracon + MapTracon).
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: <cold|first-request|idle|compose> <none|bare|full|web-none|web> [path] [idle seconds]");

    return 2;
}

var measurement = args[0];
var setup = args[1];
var path = args.Length > 2 ? args[2] : "/tracon/api/agents";
var idleSeconds = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 15;
var databaseDirectory = Directory.CreateTempSubdirectory("tracon-startup-probe-");

try
{
    return measurement switch
    {
        "cold" => await MeasureColdStartAsync().ConfigureAwait(false),
        "first-request" => await MeasureFirstRequestAsync().ConfigureAwait(false),
        "idle" => await MeasureIdleAsync().ConfigureAwait(false),
        "compose" => await MeasureComposeAsync().ConfigureAwait(false),
        _ => Unknown(measurement),
    };
}
finally
{
    databaseDirectory.Delete(recursive: true);
}

async Task<int> MeasureColdStartAsync()
{
    var before = Take();
    var clock = Stopwatch.StartNew();
    var host = Build(out var hostedCount);
    var built = Take();
    var buildMs = clock.Elapsed.TotalMilliseconds;

    await host.StartAsync().ConfigureAwait(false);
    var started = Take();
    var startMs = clock.Elapsed.TotalMilliseconds - buildMs;

    clock.Restart();
    await host.StopAsync().ConfigureAwait(false);
    var stopMs = clock.Elapsed.TotalMilliseconds;
    await DisposeAsync(host).ConfigureAwait(false);

    Report(
        ("setup", setup),
        ("hosted", hostedCount),
        ("build_ms", buildMs),
        ("start_ms", startMs),
        ("stop_ms", stopMs),
        ("jit_build", built.CompiledMethods - before.CompiledMethods),
        ("jit_start", started.CompiledMethods - built.CompiledMethods),
        ("alloc_build_kb", (built.AllocatedBytes - before.AllocatedBytes) / 1024),
        ("alloc_start_kb", (started.AllocatedBytes - built.AllocatedBytes) / 1024));

    return 0;
}

// Builds a container, resolves the hosted services and runs what a host runs before its loops start, thirty times in one
// process after five warm-up rounds, and reports the bytes one round allocates. The loops are resolved and not started:
// a started loop allocates on the thread pool, in a way that depends on timing.
async Task<int> MeasureComposeAsync()
{
    const int Rounds = 30;
    var bytes = new List<long>(Rounds);
    var milliseconds = new List<double>(Rounds);

    for (var round = 0; round < Rounds + 5; round++)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTracon();

        // The analyzer warns that a second container copies the singletons. That is the measurement here: the cost of one
        // container, built the way a host builds it, with nothing else sharing it.
#pragma warning disable ASP0000
        var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

        await using (provider.ConfigureAwait(false))
        {
            var hosted = provider.GetServices<IHostedService>().ToList();

            foreach (var service in hosted.OfType<IHostedLifecycleService>())
            {
                await service.StartingAsync(CancellationToken.None).ConfigureAwait(false);
            }

            foreach (var service in hosted)
            {
                if (service is not BackgroundService)
                {
                    await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        clock.Stop();

        if (round >= 5)
        {
            bytes.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
            milliseconds.Add(clock.Elapsed.TotalMilliseconds);
        }
    }

    bytes.Sort();
    milliseconds.Sort();

    Report(
        ("setup", "bare"),
        ("rounds", Rounds),
        ("bytes_min", bytes[0]),
        ("bytes_median", bytes[Rounds / 2]),
        ("bytes_max", bytes[^1]),
        ("distinct", bytes.Distinct().Count()),
        ("ms_median", milliseconds[Rounds / 2]));

    return 0;
}

async Task<int> MeasureFirstRequestAsync()
{
    if (!setup.StartsWith("web", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("first-request needs a web setup (web or web-none)");

        return 2;
    }

    var host = Build(out _);
    await host.StartAsync().ConfigureAwait(false);

    var app = (WebApplication)host;
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
    var target = setup is "web-none" ? "/ping" : path;

    var before = Take();
    var clock = Stopwatch.StartNew();
    using var first = await client.GetAsync(new Uri(target, UriKind.Relative)).ConfigureAwait(false);
    var firstMs = clock.Elapsed.TotalMilliseconds;
    var after = Take();

    clock.Restart();
    using var second = await client.GetAsync(new Uri(target, UriKind.Relative)).ConfigureAwait(false);
    var secondMs = clock.Elapsed.TotalMilliseconds;

    await host.StopAsync().ConfigureAwait(false);
    await DisposeAsync(host).ConfigureAwait(false);

    Report(
        ("setup", setup),
        ("path", target),
        ("status", (int)first.StatusCode),
        ("first_ms", firstMs),
        ("first_jit", after.CompiledMethods - before.CompiledMethods),
        ("first_alloc_kb", (after.AllocatedBytes - before.AllocatedBytes) / 1024),
        ("second_ms", secondMs));

    return 0;
}

async Task<int> MeasureIdleAsync()
{
    var host = Build(out _);
    await host.StartAsync().ConfigureAwait(false);

    // Start-up work that runs after StartAsync returns (the schema gate, a first sweep) settles first.
    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

    using var process = Process.GetCurrentProcess();
    var workItems = ThreadPool.CompletedWorkItemCount;
    var allocated = GC.GetTotalAllocatedBytes(precise: true);
    process.Refresh();
    var cpu = process.TotalProcessorTime;

    await Task.Delay(TimeSpan.FromSeconds(idleSeconds)).ConfigureAwait(false);

    process.Refresh();
    var cpuPerSecond = (process.TotalProcessorTime - cpu).TotalMilliseconds / idleSeconds;
    var workItemsPerSecond = (ThreadPool.CompletedWorkItemCount - workItems) / (double)idleSeconds;
    var allocatedPerSecond = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1024.0 / idleSeconds;

    await host.StopAsync().ConfigureAwait(false);
    await DisposeAsync(host).ConfigureAwait(false);

    Report(
        ("setup", setup),
        ("window_s", idleSeconds),
        ("work_items_per_s", workItemsPerSecond),
        ("cpu_ms_per_s", cpuPerSecond),
        ("alloc_kb_per_s", allocatedPerSecond),
        ("active_timers", Timer.ActiveCount));

    return 0;
}

IHost Build(out int hostedCount)
{
    if (setup.StartsWith("web", StringComparison.Ordinal))
    {
        var web = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        web.Logging.ClearProviders();
        web.WebHost.UseUrls("http://127.0.0.1:0");

        if (setup is "web")
        {
            web.Services.AddTracon();
        }

        hostedCount = web.Services.Count(static descriptor => descriptor.ServiceType == typeof(IHostedService));
        var app = web.Build();

        if (setup is "web")
        {
            app.MapTracon("/tracon");
        }
        else
        {
            app.MapGet("/ping", static () => "pong");
        }

        return app;
    }

    var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

    if (setup is "bare" or "full")
    {
        var tracon = builder.Services.AddTracon();

        if (setup is "full")
        {
            var database = Path.Combine(databaseDirectory.FullName, "probe.db");
            tracon.UseWorkflows().UseMcp().UseSqlite($"Data Source={database}");
        }
    }

    hostedCount = builder.Services.Count(static descriptor => descriptor.ServiceType == typeof(IHostedService));

    return builder.Build();
}

static async Task DisposeAsync(IHost host)
{
    if (host is IAsyncDisposable asyncDisposable)
    {
        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
    }
    else
    {
        host.Dispose();
    }
}

static void Report(params (string Name, object Value)[] values)
    => Console.WriteLine(string.Join(
        ' ',
        values.Select(static pair => pair.Value switch
        {
            double number => string.Create(CultureInfo.InvariantCulture, $"{pair.Name}={number:F1}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{pair.Name}={pair.Value}"),
        })));

static int Unknown(string measurement)
{
    Console.Error.WriteLine($"unknown measurement '{measurement}': use cold, first-request or idle");

    return 2;
}

// One reading of the two counters that start-up cost moves.
static (long CompiledMethods, long AllocatedBytes) Take()
    => (JitInfo.GetCompiledMethodCount(), GC.GetTotalAllocatedBytes(precise: true));
