using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon.Capacity;

/// <summary>One model call as the process that served it recorded it.</summary>
/// <remarks>
/// <para>
/// 🚨 This is the evidence for the worker axis. The scheduler nulls
/// <c>jobs.lease_owner</c> when a job completes, so the database cannot say
/// afterwards <em>which</em> process ran a job. The process that actually
/// executed the model call writes this line with its own pid, which is what
/// makes "the work was distributed" a measurement rather than an inference -
/// and what makes an overlapping execution detectable at all.
/// </para>
/// </remarks>
public sealed class CapacityExecution
{
    /// <summary>The run's correlation value, carried from request through tool argument to answer.</summary>
    public string Correlation { get; set; } = "";

    /// <summary>The Tracon run id, when the execution knew it.</summary>
    public string? RunId { get; set; }

    /// <summary>The tenant the run belonged to.</summary>
    public string Tenant { get; set; } = "";

    /// <summary>The process id that served the call.</summary>
    public int Pid { get; set; }

    /// <summary>The process's label, e.g. <c>host</c> or <c>worker-2</c>.</summary>
    public string Process { get; set; } = "";

    /// <summary>When the execution started, on that process's clock, as UTC ticks.</summary>
    public long StartedTicks { get; set; }

    /// <summary>When it finished, as UTC ticks.</summary>
    public long CompletedTicks { get; set; }

    /// <summary>How many model turns it took.</summary>
    public int Turns { get; set; }

    /// <summary>How long the provider boundary actually spent, in milliseconds.</summary>
    public double ModelMilliseconds { get; set; }

    /// <summary>How many times the code tool ran inside it.</summary>
    public int ToolCalls { get; set; }

    /// <summary>Whether the call was streamed.</summary>
    public bool Streaming { get; set; }
}

/// <summary>The serializer context for the execution log.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CapacityExecution))]
public sealed partial class CapacityExecutionJson : JsonSerializerContext;

/// <summary>Appends executions to one file per process.</summary>
/// <remarks>
/// 🚨 One file <em>per process</em>, not one shared file. Several operating-system
/// processes appending to the same file interleave partial lines under load,
/// and a torn line in the evidence file would look exactly like a lost
/// execution. The driver reads the whole directory and merges.
/// </remarks>
public sealed class CapacityExecutionLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly Lock _gate = new();

    /// <summary>Opens this process's log file inside the given directory.</summary>
    /// <param name="directory">The directory, or <see langword="null"/> to record nothing.</param>
    /// <param name="processName">This process's label.</param>
    public CapacityExecutionLog(string? directory, string processName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{processName}-{Environment.ProcessId}.jsonl");

        _writer = new StreamWriter(Path.Combine(directory, name), append: true) { AutoFlush = true };
    }

    /// <summary>Appends one execution.</summary>
    /// <param name="execution">What the process served.</param>
    public void Append(CapacityExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        if (_writer is null)
        {
            return;
        }

        var line = JsonSerializer.Serialize(execution, CapacityExecutionJson.Default.CapacityExecution);

        lock (_gate)
        {
            _writer.WriteLine(line);
        }
    }

    /// <summary>Reads every execution written into a directory, by any process.</summary>
    /// <param name="directory">The directory the processes wrote into.</param>
    /// <returns>Every execution, in file order.</returns>
    public static IReadOnlyList<CapacityExecution> ReadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var executions = new List<CapacityExecution>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                // A torn final line is skipped rather than aborting the read:
                // a process killed mid-write must not destroy the evidence the
                // other processes produced. The count difference shows up in
                // the reconciliation.
                try
                {
                    if (JsonSerializer.Deserialize(line, CapacityExecutionJson.Default.CapacityExecution) is { } execution)
                    {
                        executions.Add(execution);
                    }
                }
                catch (JsonException)
                {
                    continue;
                }
            }
        }

        return executions;
    }

    /// <inheritdoc />
    public void Dispose() => _writer?.Dispose();
}
