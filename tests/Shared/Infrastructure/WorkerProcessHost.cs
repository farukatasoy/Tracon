using System.Globalization;

namespace AgentPrism.Tests.Common;

/// <summary>
/// Starts <c>tests/AgentPrism.WorkerHarness</c> as a separate operating-system
/// process - a real AgentPrism host a test can kill.
/// </summary>
/// <remarks>
/// <para>
/// It builds on <see cref="ProcessRunner"/> rather than starting a process of
/// its own: the MSBuild node-reuse deadlock fix documented there applies to
/// every redirected subprocess in this repository, and a second
/// <c>ProcessStartInfo</c> builder would be exactly the synchronization copy
/// this repository has paid for five times.
/// </para>
/// <para>
/// 🚨 The harness is located by build output path, not by <c>dotnet run</c>.
/// <c>dotnet run</c> would start MSBuild inside the test, adding seconds of
/// noise to a measurement whose whole subject is a lease clock, and its
/// process tree would put a launcher between the test and the worker - so a
/// kill would land on the launcher instead of the host.
/// </para>
/// </remarks>
internal static class WorkerProcessHost
{
    private const string ProjectName = "AgentPrism.WorkerHarness";

#if DEBUG
    private const string Configuration = "debug";
#else
    private const string Configuration = "release";
#endif

    /// <summary>Starts a harness process and waits until it reports ready.</summary>
    /// <param name="options">What the process should connect to and how it should behave.</param>
    /// <returns>The running process handle.</returns>
    public static Task<ManagedProcess> StartAsync(WorkerProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var executable = ResolveExecutable();

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkerHarnessContract.ModeVariable] = options.RunWorker
                ? WorkerHarnessContract.WorkerMode
                : WorkerHarnessContract.ApiMode,
            [WorkerHarnessContract.ConnectionVariable] = options.ConnectionString,
            [WorkerHarnessContract.SchemaVariable] = options.SchemaName,
            [WorkerHarnessContract.LogVariable] = options.ExecutionLogPath,
            [WorkerHarnessContract.NameVariable] = options.Name,
            [WorkerHarnessContract.LeaseSecondsVariable] = Seconds(options.LeaseDuration),
            [WorkerHarnessContract.PollSecondsVariable] = Seconds(options.PollInterval),
            [WorkerHarnessContract.WorkSecondsVariable] = Seconds(options.WorkDuration),
            [WorkerHarnessContract.MaxAttemptsVariable] = options.MaxAttempts.ToString(CultureInfo.InvariantCulture),
        };

        return ProcessRunner.StartAsync(
            executable,
            arguments: string.Empty,
            readyLine: WorkerHarnessContract.ReadyLine,
            readyTimeout: TimeSpan.FromSeconds(60),
            workingDirectory: Path.GetDirectoryName(executable),
            environment: environment);
    }

    private static string ResolveExecutable()
    {
        var name = OperatingSystem.IsWindows() ? ProjectName + ".exe" : ProjectName;

        var path = Path.Combine(RepoRoot.Path, "artifacts", "bin", ProjectName, Configuration, name);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The worker harness was not built at '{path}'. The test project references " +
                $"{ProjectName} so the build produces it; run the suite through a build rather " +
                "than against stale output.",
                path);
        }

        return path;
    }

    private static string Seconds(TimeSpan value)
        => value.TotalSeconds.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The settings one harness process is started with.</summary>
internal sealed record WorkerProcessOptions
{
    /// <summary>The label this process writes into the shared execution log.</summary>
    public required string Name { get; init; }

    /// <summary>The database the process connects to.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>The schema the process reads and writes - the test migrates it first.</summary>
    public required string SchemaName { get; init; }

    /// <summary>The file every execution appends one line to.</summary>
    public required string ExecutionLogPath { get; init; }

    /// <summary>
    /// Whether this process leases jobs. <see langword="false"/> is the API
    /// node shape: the same stores, no leasing.
    /// </summary>
    public bool RunWorker { get; init; } = true;

    /// <summary>How long a lease this process's worker takes.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the worker looks for leasable work.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the harness handler takes per item.</summary>
    public TimeSpan WorkDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The attempt ceiling the worker applies.</summary>
    public int MaxAttempts { get; init; } = 3;
}
