namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every <c>new PeriodicTimer(</c> in <c>src/</c> must be a known site whose
/// period is either a constant or an option checked at startup (F-278).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PeriodicTimer"/> refuses a period outside one millisecond to
/// about 49.7 days, and every site below creates its timer inside a hosted
/// service, where .NET's default <c>StopHost</c> turns that exception into a
/// stopped host after "Application started". The defect appeared twice
/// (gauge refresh intervals, then nine more intervals in one class scan),
/// each time because a new periodic loop was added without a validator.
/// </para>
/// <para>
/// This gate does not read the code: it makes a NEW timer site fail until
/// someone names the option that feeds it and the validator that checks it
/// with <c>TimerPeriod.IsValid</c> (the checks themselves are proven by
/// <c>TimerPeriodOptionValidationTests</c>). A site that disappears fails too,
/// so the list cannot go stale. <c>Task.Delay</c> loops are not scanned; the
/// one that takes a configured interval (MCP discovery) has its own test.
/// </para>
/// </remarks>
public sealed class PeriodicTimerSiteTests
{
    private static readonly Dictionary<string, string> KnownSites = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Core/Approvals/ApprovalExpirationService.cs"] = "TraconApprovalOptions.ScanInterval — TraconApprovalOptionsValidator",
        ["src/Tracon.Core/Coordination/SingletonGuard.cs"] = "SingletonExecutionOptions.LeaseDuration / 3 — SingletonExecutionOptionsValidator",
        ["src/Tracon.Core/Diagnostics/CachedGaugeSource.cs"] = "TraconObservabilityOptions gauge intervals — TraconOptionsValidator",
        ["src/Tracon.Core/Experiments/CanaryEvaluationService.cs"] = "CanaryOptions.ScanInterval — CanaryOptionsValidator",
        ["src/Tracon.Core/Hosting/TraconDrainService.cs"] = "constant 200 ms",
        ["src/Tracon.Core/Models/ModelProviderHealthBackgroundService.cs"] = "TraconHealthOptions.BackgroundInterval — TraconOptionsValidator",
        ["src/Tracon.Core/Recording/RunHeartbeatWriter.cs"] = "RunReconciliationOptions.HeartbeatInterval — RunReconciliationOptionsValidator",
        ["src/Tracon.Core/Recording/RunReconciliationService.cs"] = "RunReconciliationOptions.ScanInterval — RunReconciliationOptionsValidator",
        ["src/Tracon.Core/Scheduling/JobWorkerBackgroundService.cs"] = "TraconSchedulingOptions.PollInterval and LeaseDuration / 2 — TraconSchedulingOptionsValidator",
    };

    [Fact]
    public void Every_periodic_timer_site_is_known_and_checked_at_startup()
    {
        var root = FindRepositoryRoot();

        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("new PeriodicTimer(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = actual.Where(path => !KnownSites.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var gone = KnownSites.Keys.Where(path => !actual.Contains(path)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A new PeriodicTimer site was added. Check the option that feeds its period with " +
            "TimerPeriod.IsValid in a validator registered with ValidateOnStart, prove it in " +
            "TimerPeriodOptionValidationTests, then add the file here with that option's name.");
        gone.ShouldBeEmpty("A known PeriodicTimer site no longer creates a timer; remove it from the list.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
