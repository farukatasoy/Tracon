namespace Tracon.Capacity;

/// <summary>The names the capacity host, driver and acceptance suite agree on.</summary>
/// <remarks>
/// 🚨 This file is <strong>linked</strong> into every capacity project, never
/// copied. A second copy of these names is the synchronization copy this
/// repository has paid for five times: the two sides would still compile, the
/// host would answer on one route and the driver would ask on another, and the
/// run would report a transport failure that is really a typo.
/// </remarks>
public static class CapacityContract
{
    /// <summary>The line the host prints once it is listening. The driver waits for this line, never for a duration.</summary>
    public const string HostReadyLine = "CAPACITY-HOST-READY";

    /// <summary>The line a worker-only process prints once it has subscribed to the queue.</summary>
    public const string WorkerReadyLine = "CAPACITY-WORKER-READY";

    /// <summary>The Tracon prefix the host mounts.</summary>
    public const string Prefix = "/tracon";

    /// <summary>The apparatus surface the host adds beside Tracon's own.</summary>
    public const string ApparatusPrefix = "/capacity";

    /// <summary>The apparatus endpoint that reports what the host actually measured.</summary>
    public const string TelemetryRoute = ApparatusPrefix + "/telemetry";

    /// <summary>The apparatus endpoint that reports the host's effective settings.</summary>
    public const string SettingsRoute = ApparatusPrefix + "/settings";

    /// <summary>The apparatus endpoint that resets the host's own counters between phases.</summary>
    public const string ResetRoute = ApparatusPrefix + "/reset";

    /// <summary>The catalog name of the agent under load.</summary>
    public const string AgentName = "capacity-agent";

    /// <summary>The model provider the host registers.</summary>
    public const string ProviderName = "capacity";

    /// <summary>The model the agent binds to.</summary>
    public const string ModelName = "capacity-1";

    /// <summary>The tenant header the host resolves, only at the loopback fixture boundary.</summary>
    public const string TenantHeader = "X-Tracon-Tenant";

    /// <summary>The first synthetic tenant.</summary>
    public const string TenantA = "capacity-a";

    /// <summary>The second synthetic tenant.</summary>
    public const string TenantB = "capacity-b";

    /// <summary>The environment variable carrying the database connection string.</summary>
    /// <remarks>🚨 A connection string is never a process argument, so it never reaches a process listing.</remarks>
    public const string ConnectionVariable = "TRACON_CAPACITY_CONNECTION";

    /// <summary>The environment variable carrying the schema the process owns.</summary>
    public const string SchemaVariable = "TRACON_CAPACITY_SCHEMA";

    /// <summary>The environment variable selecting the host's mode.</summary>
    public const string ModeVariable = "TRACON_CAPACITY_MODE";

    /// <summary>The environment variable carrying the workload as JSON.</summary>
    public const string WorkloadVariable = "TRACON_CAPACITY_WORKLOAD";

    /// <summary>The environment variable carrying the port the host listens on.</summary>
    public const string PortVariable = "TRACON_CAPACITY_PORT";

    /// <summary>The environment variable naming this process in claim records.</summary>
    public const string NameVariable = "TRACON_CAPACITY_NAME";

    /// <summary>The environment variable carrying the seed shape for the seed mode.</summary>
    public const string SeedVariable = "TRACON_CAPACITY_SEED_RUNS";

    /// <summary>The environment variable carrying the connection pool ceiling.</summary>
    public const string PoolVariable = "TRACON_CAPACITY_POOL";

    /// <summary>The API host mode: mounts Tracon's HTTP surface.</summary>
    public const string ApiMode = "api";

    /// <summary>The worker-only mode: leases jobs, mounts no HTTP surface.</summary>
    public const string WorkerMode = "worker";

    /// <summary>The seed mode: writes the full fixture, then exits.</summary>
    public const string SeedMode = "seed";

    /// <summary>The migrate mode: applies the schema once, then exits.</summary>
    /// <remarks>
    /// 🚨 A separate invocation on purpose. If the measured host migrated on
    /// startup, schema creation would sit inside what the first cell measures,
    /// and every worker process would race for the same migration lock.
    /// </remarks>
    public const string MigrateMode = "migrate";

    /// <summary>The line the migrate mode prints once the schema is ready.</summary>
    public const string MigratedLine = "CAPACITY-MIGRATED";
}
