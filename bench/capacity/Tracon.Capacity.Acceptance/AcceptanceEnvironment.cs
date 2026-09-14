using System.Globalization;

namespace Tracon.Capacity.Acceptance;

/// <summary>What the orchestrator handed this suite.</summary>
/// <remarks>
/// 🚨 Every value is REQUIRED. A missing prerequisite throws rather than
/// skipping: a skipped provenance check reads exactly like a passing one in a
/// CI log, and provenance is the claim this suite exists to make.
/// </remarks>
public static class AcceptanceEnvironment
{
    /// <summary>The host's Tracon prefix, e.g. <c>http://127.0.0.1:5199/tracon</c>.</summary>
    public static string BaseAddress => Required("TRACON_CAPACITY_ACCEPTANCE_BASE");

    /// <summary>The database the host writes to.</summary>
    public static string ConnectionString => Required("TRACON_CAPACITY_CONNECTION");

    /// <summary>The schema the host owns.</summary>
    public static string Schema => Required("TRACON_CAPACITY_SCHEMA");

    /// <summary>The exact package version under measurement.</summary>
    public static string PackageVersion => Required("TRACON_CAPACITY_VERSION");

    /// <summary>The host's <c>project.assets.json</c>, the restore's own record.</summary>
    public static string AssetsPath => Required("TRACON_CAPACITY_ASSETS");

    /// <summary>The isolated package cache the restore was told to use.</summary>
    public static string PackageCache => Required("TRACON_CAPACITY_CACHE");

    /// <summary>The host's build output directory.</summary>
    public static string HostOutputDirectory => Required("TRACON_CAPACITY_HOSTDIR");

    /// <summary>The run's artifact directory, scanned for leaked credentials.</summary>
    public static string ArtifactDirectory => Required("TRACON_CAPACITY_ARTIFACTS");

    /// <summary>A synthetic credential planted in the host's environment.</summary>
    public static string Canary => Required("TRACON_CAPACITY_CANARY");

    /// <summary>Where the host processes wrote their execution records.</summary>
    public static string ExecutionDirectory => Required("TRACON_CAPACITY_EXECUTIONS");

    private static string Required(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{name}' is not set. This suite is run by `kapi.py kapasite --profil smoke`, " +
                $"which prepares the packed host, the database and the artifact directory. " +
                $"A missing prerequisite is a failed precondition, never a skipped test."));
}
