using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures the allocation of writing one streamed event -
/// <see cref="RunEventWriter.AppendAsync"/> is called once per streaming delta
/// in a real run, so its own per-call cost is the hot path
/// (docs/arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md, 116.2).
/// </summary>
[MemoryDiagnoser]
public class RunEventWriterBenchmarks
{
    private RunEventWriter _writer = null!;
    private RunEventDraft _delta;

    [GlobalSetup]
    public void Setup()
    {
        _writer = new RunEventWriter(
            new NoOpRunStore(),
            new TraconRunRecordingOptions(),
            NullLogger.Instance,
            Guid.NewGuid());

        _delta = new RunEventDraft(RunEventType.MessageDelta) { Text = "a reasonably sized streamed text delta" };
    }

    [Benchmark]
    public ValueTask<RunEvent> AppendEvent() => _writer.AppendAsync(_delta);
}
