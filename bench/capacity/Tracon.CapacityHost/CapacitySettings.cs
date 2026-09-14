using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracon.Capacity;

namespace Tracon.CapacityHost;

/// <summary>What one host process was told to be.</summary>
/// <remarks>
/// 🚨 Every value arrives through the environment, never through a process
/// argument: the connection string is one of them, and a process argument is
/// visible to anyone who can list processes (K-059's boundary applied to the
/// apparatus).
/// </remarks>
public sealed record CapacitySettings
{
    /// <summary><c>api</c>, <c>worker</c> or <c>seed</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>How to reach the database.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>The schema this process owns.</summary>
    public required string Schema { get; init; }

    /// <summary>The label this process writes into its execution records.</summary>
    public required string Name { get; init; }

    /// <summary>The loopback port the API mode listens on.</summary>
    public int Port { get; init; }

    /// <summary>Where the execution records go, one file per process.</summary>
    public string? ExecutionLogDirectory { get; init; }

    /// <summary>The synthetic workload this process replays.</summary>
    public required HostWorkload Workload { get; init; }

    /// <summary>The connection pool ceiling, stated explicitly rather than inherited.</summary>
    public int MaxPoolSize { get; init; } = 100;

    /// <summary>How many completed runs the seed mode writes.</summary>
    public int SeedRuns { get; init; }

    /// <summary>Reads the settings this process was started with.</summary>
    /// <returns>The settings.</returns>
    /// <exception cref="InvalidOperationException">A required variable is missing.</exception>
    public static CapacitySettings FromEnvironment()
    {
        var workloadJson = Environment.GetEnvironmentVariable(CapacityContract.WorkloadVariable);

        return new CapacitySettings
        {
            Mode = Required(CapacityContract.ModeVariable),
            ConnectionString = Required(CapacityContract.ConnectionVariable),
            Schema = Required(CapacityContract.SchemaVariable),
            Name = Environment.GetEnvironmentVariable(CapacityContract.NameVariable) ?? "host",
            Port = Integer(CapacityContract.PortVariable, 0),
            ExecutionLogDirectory = Environment.GetEnvironmentVariable("TRACON_CAPACITY_EXECUTIONS"),
            MaxPoolSize = Integer(CapacityContract.PoolVariable, 100),
            SeedRuns = Integer(CapacityContract.SeedVariable, 0),
            Workload = string.IsNullOrWhiteSpace(workloadJson)
                ? new HostWorkload()
                : JsonSerializer.Deserialize(workloadJson, HostWorkloadJson.Default.HostWorkload) ?? new HostWorkload(),
        };
    }

    private static string Required(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"The environment variable '{name}' is required.");

    private static int Integer(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}

/// <summary>The synthetic workload, as the host receives it.</summary>
/// <remarks>
/// A mirror of the driver's own <c>WorkloadSpec</c> by VALUE, not by type: the
/// host cannot reference the driver (they are separate processes with separate
/// dependency graphs), and the JSON that crosses between them is the contract.
/// Its field names are asserted by the acceptance suite.
/// </remarks>
public sealed class HostWorkload
{
    /// <summary>Bytes of UTF-8 in the request message.</summary>
    public int RequestBytes { get; set; } = 1024;

    /// <summary>Total milliseconds the model provider waits across all turns.</summary>
    public int ModelDelayMilliseconds { get; set; } = 1000;

    /// <summary>How many chunks the final answer is made of.</summary>
    public int OutputChunks { get; set; } = 20;

    /// <summary>ASCII characters per chunk.</summary>
    public int OutputChunkCharacters { get; set; } = 256;

    /// <summary>Bytes the code tool returns.</summary>
    public int ToolResultBytes { get; set; } = 1024;

    /// <summary>Whether the model calls the tool before it can answer.</summary>
    public bool ToolCall { get; set; } = true;
}

/// <summary>The serializer context for the workload contract.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HostWorkload))]
public sealed partial class HostWorkloadJson : JsonSerializerContext;
