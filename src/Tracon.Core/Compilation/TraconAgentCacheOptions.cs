namespace Tracon;

/// <summary>Defines the bounds of the compiled agent cache.</summary>
/// <remarks>
/// <para>
/// The cache keeps one compiled agent per tenant, agent name, definition and culture, and a compiled agent
/// weighs about 8 KiB with no tools and a short instruction, and about 42 KiB with sixty tools and an 8 KiB
/// instruction (measured; the instruction text counts about four bytes per character, because the compiled
/// agent holds more than one copy of it). Without a bound, an installation that accepts any tenant
/// identifier (an empty <c>AllowedTenants</c> list and a tenant read from a header) lets the caller choose
/// how many entries the cache holds.
/// </para>
/// <para>
/// An entry that leaves the cache is not disposed. A run that already holds the agent keeps using it, and the
/// next run that needs the same agent compiles it again, which costs a fraction of a millisecond. Both limits
/// are read when the host starts; changing them afterwards does not apply to the running process.
/// </para>
/// </remarks>
public sealed class TraconAgentCacheOptions
{
    /// <summary>The default of <see cref="MaxEntries"/>: 4096.</summary>
    public const int DefaultMaxEntries = 4096;

    /// <summary>
    /// Gets or sets the most entries the cache holds. The default is 4096, which is about 34 MiB for agents with
    /// no tools and about 68 MiB for an agent with five tools and a 2 KiB instruction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When a new entry would pass the limit, the entries used least recently are dropped until the cache is
    /// about a tenth below it, so the cost of choosing them is paid once for many new entries. A single-tenant
    /// installation with a hundred agents, three cultures and three live versions of each holds 900 entries and
    /// never reaches the limit.
    /// </para>
    /// <para>Must be from 1 to 1,000,000; any other value fails at startup.</para>
    /// </remarks>
    public int MaxEntries { get; set; } = DefaultMaxEntries;

    /// <summary>
    /// Gets or sets how long an entry may go unused before it is dropped. The default is one hour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A use of the entry (a run that resolves the agent) starts the time again. The entries are checked while
    /// the cache is in use, at most once every quarter of this time and no more than once a second, so an entry
    /// can outlive the limit by that much. The cache has no timer of its own: a process that is idle keeps
    /// what it holds until the next resolve.
    /// </para>
    /// <para>
    /// An old version of a definition that is saved over is never read again, and this limit is what removes
    /// its entry.
    /// </para>
    /// <para>Must be greater than zero and at most 30 days; any other value fails at startup.</para>
    /// </remarks>
    public TimeSpan IdleLifetime { get; set; } = TimeSpan.FromHours(1);
}
