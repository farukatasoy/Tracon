using System.Globalization;
using Tracon.Tests.Common;

namespace Tracon.WorkerHarness;

/// <summary>
/// Everything the harness process reads from its environment.
/// </summary>
/// <remarks>
/// The settings arrive as environment variables rather than command-line
/// arguments so that a connection string carrying a password never appears in
/// a process listing. The harness writes no secret anywhere: the execution log
/// records job identity only.
/// </remarks>
internal sealed record WorkerHarnessSettings
{
    public required WorkerHarnessMode Mode { get; init; }

    public required string ConnectionString { get; init; }

    public required string SchemaName { get; init; }

    /// <summary>The shared file every execution appends one line to.</summary>
    public required string ExecutionLogPath { get; init; }

    /// <summary>This process's label in the execution log.</summary>
    public required string Name { get; init; }

    public required TimeSpan LeaseDuration { get; init; }

    public required TimeSpan PollInterval { get; init; }

    /// <summary>How long the handler pretends to work before reporting its item.</summary>
    public required TimeSpan WorkDuration { get; init; }

    public required int MaxAttempts { get; init; }

    public static WorkerHarnessSettings FromEnvironment()
    {
        return new WorkerHarnessSettings
        {
            Mode = Read(WorkerHarnessContract.ModeVariable) switch
            {
                WorkerHarnessContract.WorkerMode => WorkerHarnessMode.Worker,
                WorkerHarnessContract.ApiMode => WorkerHarnessMode.Api,
                var other => throw new InvalidOperationException(
                    $"{WorkerHarnessContract.ModeVariable} must be 'worker' or 'api'; it was '{other}'."),
            },
            ConnectionString = Read(WorkerHarnessContract.ConnectionVariable),
            SchemaName = Read(WorkerHarnessContract.SchemaVariable),
            ExecutionLogPath = Read(WorkerHarnessContract.LogVariable),
            Name = Read(WorkerHarnessContract.NameVariable),
            LeaseDuration = ReadSeconds(WorkerHarnessContract.LeaseSecondsVariable),
            PollInterval = ReadSeconds(WorkerHarnessContract.PollSecondsVariable),
            WorkDuration = ReadSeconds(WorkerHarnessContract.WorkSecondsVariable),
            MaxAttempts = int.Parse(Read(WorkerHarnessContract.MaxAttemptsVariable), CultureInfo.InvariantCulture),
        };
    }

    private static string Read(string name)
        => Environment.GetEnvironmentVariable(name)
           ?? throw new InvalidOperationException($"The environment variable {name} is not set.");

    private static TimeSpan ReadSeconds(string name)
        => TimeSpan.FromSeconds(double.Parse(Read(name), CultureInfo.InvariantCulture));
}

/// <summary>The process shape the harness runs as.</summary>
internal enum WorkerHarnessMode
{
    /// <summary>Leases and executes jobs - a worker node.</summary>
    Worker,

    /// <summary>Registers the same stores but leases nothing - an API node.</summary>
    Api,
}
