using System.Runtime.CompilerServices;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using Tracon.Benchmarks;

// The artifacts path is repo-relative on purpose; RepositoryLayout carries the
// reason, and the reason a compile-time path alone is NOT enough (deterministic
// builds rewrite it to `/_`, which cost the first tag push an `exit 134`).
static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    => RepositoryLayout.ResolveRoot(sourceFilePath, AppContext.BaseDirectory);

// A plain config, not DefaultConfig.Instance: the default adds every exporter
// (HTML, CSV, markdown) and a disassembly diagnoser this gate never reads.
// `scripts/kapi.py performans` parses only the FULL JSON export (BytesAllocatedPerOperation).
var config = ManualConfig.CreateEmpty()
    .AddJob(BenchmarkDotNet.Jobs.Job.Default)
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full)
    .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
    .AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance)
    .WithArtifactsPath(Path.Combine(RepositoryRoot(), "artifacts", "benchmarks"));

BenchmarkSwitcher.FromAssembly(System.Reflection.Assembly.GetExecutingAssembly()).Run(args, config);
