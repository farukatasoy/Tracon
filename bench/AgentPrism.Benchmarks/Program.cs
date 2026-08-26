using System.Runtime.CompilerServices;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

// A fixed, repo-relative artifacts path: BenchmarkDotNet's own default
// (derived from the executing assembly's location) lands somewhere different
// depending on HOW the project is launched (`dotnet run` vs the built exe
// directly), which would make `scripts/kapi.py performans`'s report glob
// unreliable. [CallerFilePath] gives this source file's own absolute path AT
// BUILD TIME on whichever machine compiles it - always this checkout's path,
// since build and run happen on the same machine.
static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", ".."));

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
