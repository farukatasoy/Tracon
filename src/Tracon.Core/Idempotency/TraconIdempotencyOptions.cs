namespace Tracon;

/// <summary>Options for <c>Idempotency-Key</c> support.</summary>
/// <remarks>
/// <para>
/// Reads values from the <c>Tracon:Idempotency</c> configuration section.
/// </para>
/// <para>
/// The <strong>default is enabled</strong>, and that is a deliberate reading of the
/// no-surprises rule.
/// Unlike rate limiting and quotas, this feature activates only when a client sends
/// the <c>Idempotency-Key</c> header. A request without the header has no extra cost
/// or behavior change. If disabled by default, a client that sends the header could
/// assume protection when it has none. That would be the silent surprise.
/// </para>
/// </remarks>
public sealed class TraconIdempotencyOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:Idempotency";

    /// <summary>
    /// Gets or sets a value that enables idempotency support. Even when enabled, a
    /// request without an <c>Idempotency-Key</c> header has no extra cost. Default: <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The maximum key length, in characters (UTF-16 code units). The endpoint returns <c>400</c> when it is exceeded.
    /// Default: 200.
    /// </summary>
    /// <remarks>
    /// The default is the width of the narrowest column that stores a key (SQL Server keeps it in an
    /// <c>nvarchar(200)</c>), so a key over the limit is refused for what it is, with the same answer on every store. A
    /// value above 200 is accepted by the stores that keep text of any length; on SQL Server a key between 200 and the
    /// value passes this check and the store refuses it with <c>400</c> as well.
    /// </remarks>
    public int MaxKeyLength { get; set; } = 200;

    /// <summary>
    /// Gets or sets how old a reserved key that was never completed has to be before the next request with the same key
    /// takes it over. Zero (or less) never takes one over. Default: one hour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request reserves its key, runs, and then stores its response. A process that dies in between leaves a key nobody
    /// finishes, and every later request with it answers <c>409</c> until the record is deleted by retention. After this
    /// time the next request takes the key and runs.
    /// </para>
    /// <para>
    /// Set it longer than the longest request that carries an <c>Idempotency-Key</c>. A request that is still running when
    /// its key is taken over runs a second time, which is the very thing the key is there to prevent.
    /// </para>
    /// </remarks>
    public TimeSpan ReservationTimeout { get; set; } = TimeSpan.FromHours(1);
}
