namespace Tracon;

/// <summary>Orphaned-run reconciliation settings.</summary>
/// <remarks>
/// Read from the <c>Tracon:RunReconciliation</c> configuration section.
/// See <c>TraconServiceCollectionExtensions.AddTracon</c>.
/// </remarks>
public sealed class RunReconciliationOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:RunReconciliation";

    /// <summary>
    /// Whether reconciliation is on. Defaults to <see langword="false"/>: in a
    /// single-instance development setup, no one expects a background writer,
    /// and no query goes to the lease table.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The interval at which a running run writes its "I'm still here" signal.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A <c>Running</c> row that has not signalled for longer than this is
    /// considered orphaned.
    /// </summary>
    /// <remarks>
    /// Defaults to ten times <see cref="HeartbeatInterval"/>: a single GC
    /// pause or a brief database interruption must NOT declare a job running dead.
    /// </remarks>
    public TimeSpan OrphanThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The wait between reconciliation passes.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The maximum number of rows to close in one pass.</summary>
    public int MaxRunsPerScan { get; set; } = 100;
}
