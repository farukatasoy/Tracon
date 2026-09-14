namespace Tracon.CapacityDriver;

/// <summary>Everything needed to know whether two capacity runs may be compared.</summary>
/// <remarks>
/// <para>
/// 🚨 The manifest is what stops a number from travelling without its
/// conditions. A latency figure from one machine, one PostgreSQL version and
/// one configuration says nothing about another; comparison is refused unless
/// the identities below match.
/// </para>
/// <para>
/// 🚨 It never carries a connection string, a credential, a raw environment
/// dump, or user content. `scripts/capacity.py` builds it and the driver
/// scrubs whatever it writes.
/// </para>
/// </remarks>
public sealed class RunManifest
{
    /// <summary>The run's identifier, which is also its artifact directory name.</summary>
    public string RunId { get; set; } = "";

    /// <summary>The profile that was run.</summary>
    public string Profile { get; set; } = "";

    /// <summary>When the run started, in UTC.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>The repository commit the packages were built from.</summary>
    public string Commit { get; set; } = "";

    /// <summary>Whether the working tree carried uncommitted changes.</summary>
    public bool Dirty { get; set; }

    /// <summary>A hash of the uncommitted diff, when there was one.</summary>
    public string? DiffHash { get; set; }

    /// <summary>The exact Tracon package version the host consumed.</summary>
    public string PackageVersion { get; set; } = "";

    /// <summary>The SHA-256 of every package the host restored from the isolated feed.</summary>
    public Dictionary<string, string> PackageHashes { get; set; } = [];

    /// <summary>The operating system the run happened on.</summary>
    public string OperatingSystem { get; set; } = "";

    /// <summary>The processor architecture.</summary>
    public string Architecture { get; set; } = "";

    /// <summary>How many logical processors the machine has.</summary>
    public int ProcessorCount { get; set; }

    /// <summary>Physical memory, in bytes, as the operating system reports it.</summary>
    public long? PhysicalMemoryBytes { get; set; }

    /// <summary>The .NET runtime version.</summary>
    public string RuntimeVersion { get; set; } = "";

    /// <summary>The PostgreSQL server version.</summary>
    public string PostgreSqlVersion { get; set; } = "";

    /// <summary>The container image the database ran in, when it ran in one.</summary>
    public string? DatabaseImage { get; set; }

    /// <summary>The effective settings every host process was started with.</summary>
    public Dictionary<string, string> EffectiveSettings { get; set; } = [];

    /// <summary>The shape of the seeded fixture.</summary>
    public Dictionary<string, string> Seed { get; set; } = [];

    /// <summary>The synthetic workload, as experiment input.</summary>
    public WorkloadSpec Workload { get; set; } = new();

    /// <summary>The resource caps in force.</summary>
    public ResourceLimits Limits { get; set; } = new();

    /// <summary>The seed a randomized workload was drawn from; the default workload is deterministic.</summary>
    public int? WorkloadSeed { get; set; }

    /// <summary>Which measurements this platform supplied, and which it did not.</summary>
    public TelemetryCoverage Telemetry { get; set; } = new();

    /// <summary>The sentence that must travel with every number this run produced.</summary>
    public string Disclaimer { get; set; } =
        "Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.";
}
