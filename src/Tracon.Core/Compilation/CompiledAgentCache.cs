using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Caches compiled agents keyed by
/// <c>(tenant, name, definition fingerprint, dependency fingerprint, culture)</c>,
/// holding one entry per <c>(tenant, name, definition fingerprint, culture)</c>, and
/// holding no more than <see cref="TraconAgentCacheOptions.MaxEntries"/> of them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The key measures CONTENT, not a version number.</strong> An earlier
/// design keyed the entry by the definition's version and relied on "a save
/// increments the version, so the entry naturally goes stale". That reasoning
/// does not hold for a DELETE followed by a CREATE of the same name: a store
/// that restarts numbering (the built-in ones did, until they kept the numbering) makes the
/// new definition version <c>1</c> again, and the
/// deleted definition's compiled agent kept answering every run - the database
/// read correctly while the run did not. Measured on a real host.
/// </para>
/// <para>
/// The version component was therefore replaced by a fingerprint of the
/// definition's own serialized content
/// (<see cref="AgentDefinitionCompiler.CreateDefinitionFingerprint"/>). Two
/// entries share a compiled agent only when the definitions that produced them
/// are byte-identical, which is exactly when sharing is correct. No explicit
/// invalidation is needed, and none exists: an entry that can no longer be
/// reached is simply never read again.
/// </para>
/// <para>
/// The same rule covers what the agent depends on outside its definition. The
/// dependency fingerprint measures skills, callable sub-agents, a shared
/// instructions block, the tenant's egress policy, and the connection each remote
/// (MCP) tool is bound to: a policy saved after an agent was cached changes the
/// key, and the next resolve compiles again under that policy; a server definition
/// changed or deleted after an agent was cached changes the key too, and the next
/// resolve binds the live connection or fails the way an uncached compile does.
/// </para>
/// <para>
/// <strong>One slot per definition, and the slots are bounded.</strong>
/// The dependency fingerprint changes without any operator action: a remote MCP
/// server that edits a tool's description or schema changes it at the next refresh.
/// Each <c>(tenant, name, definition fingerprint, culture)</c> therefore holds ONE
/// entry, and a compile under a new dependency fingerprint replaces it; the replaced
/// agent is never read again. The definition fingerprint stays a separate slot: two
/// versions of one agent served side by side (an experiment's variants) must not
/// evict each other on every run.
/// </para>
/// <para>
/// <strong>The number of slots is bounded as well</strong> (<see cref="TraconAgentCacheOptions"/>).
/// A new definition version adds a slot, and a new tenant adds one for every agent it
/// runs: an installation that accepts any tenant identifier lets the caller choose
/// how many. A compiled agent weighs between about 8 KiB and 42 KiB, so the cache
/// holds at most <see cref="TraconAgentCacheOptions.MaxEntries"/> entries and drops the
/// one used least recently when a new one would pass the limit, and it drops an entry
/// that was not used for <see cref="TraconAgentCacheOptions.IdleLifetime"/>.
/// </para>
/// <para>
/// <strong>Leaving the cache is only unlinking.</strong> A dropped agent is never disposed: a
/// run may still hold it, and the cache cannot know. The run keeps its reference, the
/// garbage collector takes the agent when the last run lets go, and the next resolve
/// compiles the agent again. Nothing a compiled agent owns needs an explicit
/// dispose, and the model client it reaches is shared with every other compile of the
/// same provider. A hit takes no lock and allocates nothing; only a store, which has
/// just paid for a compile, takes the lock, and the idle check runs while the cache is
/// in use (no timer).
/// </para>
/// <para>
/// The <c>tenant</c> component of the key is REQUIRED. Agent names are unique
/// only within a tenant (see <c>SqlAgentDefinitionStore</c>) - it is common for
/// two different tenants to have a definition with the same name (e.g.
/// <c>"support"</c>), byte-identical content, and the same dependency
/// fingerprint (an empty string for both if they use no skills/callable
/// agents). If the tenant is not included in the key,
/// resolution for the second tenant returns the FIRST tenant's compiled agent
/// (instructions, tool bindings - including the tenant identity bound into
/// e.g. <c>search_knowledge</c>): tenant isolation broken outright.
/// </para>
/// </remarks>
public sealed class CompiledAgentCache
{
    // The idle check never runs more often than this: a lifetime of a few seconds must not turn every use into a scan.
    private static readonly TimeSpan MinimumSweepInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<SlotKey, Entry> _entries = new();

    // Held by every store, the idle sweep and Clear; a hit never takes it. It is what keeps the count at or under
    // the limit at every instant: nothing adds an entry outside it, and an entry is added only after room was made.
    private readonly Lock _gate = new();

    private readonly TimeProvider _clock;
    private readonly int _maxEntries;
    private readonly int _entriesKeptWhenFull;
    private readonly long _idleTicks;
    private readonly long _sweepIntervalTicks;
    private long _nextSweep;
    private long _sequence;

    internal CompiledAgentCache()
        : this(new TraconAgentCacheOptions(), timeProvider: null)
    {
    }

    /// <summary>Creates a cache bounded by <paramref name="options"/>.</summary>
    /// <param name="options">
    /// The two bounds. The host validates them at startup; they are checked here again, so a cache built by hand
    /// cannot be unbounded.
    /// </param>
    /// <param name="timeProvider">The clock the idle lifetime is measured on; <see langword="null"/> is the system clock.</param>
    internal CompiledAgentCache(TraconAgentCacheOptions options, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxEntries, "MaxEntries must be at least 1.");
        }

        if (options.IdleLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.IdleLifetime, "IdleLifetime must be greater than zero.");
        }

        _clock = timeProvider ?? TimeProvider.System;
        _maxEntries = options.MaxEntries;

        // A full cache drops down to a tenth below the limit, so the scan that picks the entries is paid once
        // for many stores. Never above limit - 1: the new entry has to fit without passing the limit.
        _entriesKeptWhenFull = Math.Min(_maxEntries - (_maxEntries / 10), _maxEntries - 1);

        _idleTicks = ToTicks(options.IdleLifetime);
        _sweepIntervalTicks = Math.Max(_idleTicks / 4, ToTicks(MinimumSweepInterval));
        _nextSweep = _clock.GetTimestamp() + _sweepIntervalTicks;
    }

    /// <summary>Number of entries in the cache: one per tenant, agent name, definition, and culture.</summary>
    /// <remarks>It never passes <see cref="TraconAgentCacheOptions.MaxEntries"/>.</remarks>
    public int Count => _entries.Count;

    /// <summary>
    /// Retrieves the agent from the cache; if absent, produces it with
    /// <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="definitionFingerprint">
    /// Fingerprint of the definition's own content; see
    /// <see cref="AgentDefinitionCompiler.CreateDefinitionFingerprint"/>.
    /// </param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public AIAgent GetOrAdd(string tenantId, string name, string definitionFingerprint, Func<AIAgent> factory)
        => GetOrAdd(tenantId, name, definitionFingerprint, string.Empty, string.Empty, factory);

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency
    /// fingerprint; if absent, produces it with <paramref name="factory"/>
    /// and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="definitionFingerprint">
    /// Fingerprint of the definition's own content; see
    /// <see cref="AgentDefinitionCompiler.CreateDefinitionFingerprint"/>.
    /// </param>
    /// <param name="dependencyFingerprint">
    /// Current fingerprint of what the compiled agent depends on outside its
    /// definition: skills, callable sub-agents, a shared instructions block, the
    /// tenant's egress policy, and the connection each remote tool is bound to.
    /// These can change while the definition stays the same, so they need a
    /// separate key component.
    /// </param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    public AIAgent GetOrAdd(string tenantId, string name, string definitionFingerprint, string dependencyFingerprint, Func<AIAgent> factory)
        => GetOrAdd(tenantId, name, definitionFingerprint, dependencyFingerprint, string.Empty, factory);

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency fingerprint and
    /// requested culture; if absent, produces it with <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="definitionFingerprint">
    /// Fingerprint of the definition's own content; see
    /// <see cref="AgentDefinitionCompiler.CreateDefinitionFingerprint"/>.
    /// </param>
    /// <param name="dependencyFingerprint">
    /// See <see cref="GetOrAdd(string, string, string, string, Func{AIAgent})"/>.
    /// </param>
    /// <param name="culture">
    /// The culture the definition was compiled with (resolved against
    /// <see cref="AgentDefinition.InstructionsByCulture"/>).
    /// An empty string when the run requested no culture. Part of the key: two runs of the
    /// same definition in different cultures must not share a compiled agent - the requested
    /// instructions text is baked into <see cref="Microsoft.Extensions.AI.ChatOptions"/> at
    /// compile time.
    /// </param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    public AIAgent GetOrAdd(
        string tenantId,
        string name,
        string definitionFingerprint,
        string dependencyFingerprint,
        string culture,
        Func<AIAgent> factory)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definitionFingerprint);
        ArgumentNullException.ThrowIfNull(dependencyFingerprint);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(factory);

        var key = new SlotKey(tenantId, name, definitionFingerprint, culture);

        // 🚨 A plain TryGetValue on the hit path: no closure, no allocation. A hit is
        // the overwhelmingly common case once a definition is compiled once.
        if (_entries.TryGetValue(key, out var existing)
            && string.Equals(existing.DependencyFingerprint, dependencyFingerprint, StringComparison.Ordinal))
        {
            Use(existing);

            return existing.Agent;
        }

        // The factory can run more than once for the same key when two callers
        // miss together. Agent production is side-effect free, so this is not a
        // problem; Store hands both callers the instance that won.
        return Store(key, dependencyFingerprint, factory());
    }

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency
    /// fingerprint; if absent, produces it asynchronously with
    /// <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="definitionFingerprint">
    /// Fingerprint of the definition's own content; see
    /// <see cref="AgentDefinitionCompiler.CreateDefinitionFingerprint"/>.
    /// </param>
    /// <param name="dependencyFingerprint">See <see cref="GetOrAdd(string, string, string, string, Func{AIAgent})"/>.</param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    /// <remarks>
    /// Compiling now needs an async credential lookup on a
    /// cache miss. <see cref="ConcurrentDictionary{TKey,TValue}"/> has no
    /// native async <c>GetOrAdd</c>; the same "the factory may run more than
    /// once, production is side-effect free, an extra instance is discarded"
    /// guarantee as the sync overload applies here too.
    /// </remarks>
    public ValueTask<AIAgent> GetOrAddAsync(
        string tenantId,
        string name,
        string definitionFingerprint,
        string dependencyFingerprint,
        Func<ValueTask<AIAgent>> factory)
        => GetOrAddAsync(tenantId, name, definitionFingerprint, dependencyFingerprint, string.Empty, factory);

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency fingerprint and
    /// requested culture; if absent, produces it asynchronously with
    /// <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="definitionFingerprint">
    /// Fingerprint of the definition's own content; see
    /// <see cref="AgentDefinitionCompiler.CreateDefinitionFingerprint"/>.
    /// </param>
    /// <param name="dependencyFingerprint">See <see cref="GetOrAdd(string, string, string, string, Func{AIAgent})"/>.</param>
    /// <param name="culture">See <see cref="GetOrAdd(string, string, string, string, string, Func{AIAgent})"/>.</param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    public async ValueTask<AIAgent> GetOrAddAsync(
        string tenantId,
        string name,
        string definitionFingerprint,
        string dependencyFingerprint,
        string culture,
        Func<ValueTask<AIAgent>> factory)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definitionFingerprint);
        ArgumentNullException.ThrowIfNull(dependencyFingerprint);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(factory);

        var key = new SlotKey(tenantId, name, definitionFingerprint, culture);

        if (_entries.TryGetValue(key, out var existing)
            && string.Equals(existing.DependencyFingerprint, dependencyFingerprint, StringComparison.Ordinal))
        {
            Use(existing);

            return existing.Agent;
        }

        var produced = await factory().ConfigureAwait(false);

        return Store(key, dependencyFingerprint, produced);
    }

    /// <summary>Combines multiple dependency fingerprints into a single key component.</summary>
    /// <param name="first">First fingerprint.</param>
    /// <param name="second">Second fingerprint.</param>
    /// <returns>The combined fingerprint.</returns>
    /// <remarks>
    /// A separator is required: fingerprints may not have a fixed length, and a
    /// direct concatenation would let two different pairs produce the same string.
    /// </remarks>
    public static string CombineFingerprints(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return second.Length == 0 ? first : string.Concat(first, "|", second);
    }

    /// <summary>Empties the cache entirely.</summary>
    /// <remarks>The agents it held are not disposed: a run may still hold one.</remarks>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    // Without the slot, a remote tool schema that changed on every refresh
    // would have added an entry every five minutes, and none was ever removed.
    /// <summary>
    /// Stores a freshly compiled agent in its slot, replacing an entry compiled under
    /// another dependency fingerprint, and makes room for it when the cache is full.
    /// </summary>
    /// <returns>
    /// The agent the slot holds for <paramref name="dependencyFingerprint"/> afterwards:
    /// <paramref name="produced"/>, or the instance a concurrent caller stored first.
    /// </returns>
    private AIAgent Store(SlotKey key, string dependencyFingerprint, AIAgent produced)
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();

            // Idle entries go first, so a full cache does not drop one that was used a moment ago while an
            // entry nobody has asked for in an hour is still there.
            if (now >= Volatile.Read(ref _nextSweep))
            {
                SweepIdle(now);
            }

            if (_entries.TryGetValue(key, out var current))
            {
                if (string.Equals(current.DependencyFingerprint, dependencyFingerprint, StringComparison.Ordinal))
                {
                    // A concurrent caller compiled and stored the same key first: both callers get its agent.
                    current.Touch(now);

                    return current.Agent;
                }

                // The slot's other dependency fingerprint is never read again: replaced, not added to.
                _entries[key] = new Entry(dependencyFingerprint, produced, now, ++_sequence);

                return produced;
            }

            if (_entries.Count >= _maxEntries)
            {
                DropLeastRecentlyUsed(_entriesKeptWhenFull);
            }

            _entries[key] = new Entry(dependencyFingerprint, produced, now, ++_sequence);

            return produced;
        }
    }

    /// <summary>Records a use of an entry and, when the idle check is due, runs it.</summary>
    private void Use(Entry entry)
    {
        var now = _clock.GetTimestamp();

        entry.Touch(now);

        // TryEnter: a use never waits for a store or for another sweep.
        if (now >= Volatile.Read(ref _nextSweep) && _gate.TryEnter())
        {
            try
            {
                SweepIdle(now);
            }
            finally
            {
                _gate.Exit();
            }
        }
    }

    /// <summary>Drops every entry that was not used for the idle lifetime. The caller holds the gate.</summary>
    private void SweepIdle(long now)
    {
        // Another thread may have swept between the caller's check and the gate.
        if (now < Volatile.Read(ref _nextSweep))
        {
            return;
        }

        Volatile.Write(ref _nextSweep, now + _sweepIntervalTicks);

        var cutoff = now - _idleTicks;

        foreach (var pair in _entries)
        {
            // Removed only while it still is the entry that was read: a store that replaced it keeps its own.
            if (pair.Value.LastUsed <= cutoff)
            {
                _entries.TryRemove(pair);
            }
        }
    }

    /// <summary>Drops the entries used least recently until <paramref name="keep"/> are left. The caller holds the gate.</summary>
    private void DropLeastRecentlyUsed(int keep)
    {
        var pairs = _entries.ToArray();
        var drop = pairs.Length - keep;

        if (drop <= 0)
        {
            return;
        }

        // The use time is read once per entry. A hit that touches an entry while the array is sorted would
        // otherwise change a key under the sort, and a sort over keys that change can fail.
        var ranked = new Ranked[pairs.Length];

        for (var index = 0; index < pairs.Length; index++)
        {
            ranked[index] = new Ranked(pairs[index].Value.LastUsed, pairs[index].Value.Sequence, pairs[index]);
        }

        Array.Sort(ranked, static (left, right) =>
        {
            var byUse = left.LastUsed.CompareTo(right.LastUsed);

            // Entries with the same time (a clock that moved less than its resolution): the older store goes first.
            return byUse != 0 ? byUse : left.Sequence.CompareTo(right.Sequence);
        });

        for (var index = 0; index < drop; index++)
        {
            _entries.TryRemove(ranked[index].Pair);
        }
    }

    private long ToTicks(TimeSpan span)
        => (long)(span.TotalSeconds * _clock.TimestampFrequency);

    private readonly record struct SlotKey(string TenantId, string Name, string DefinitionFingerprint, string Culture);

    private readonly record struct Ranked(long LastUsed, long Sequence, KeyValuePair<SlotKey, Entry> Pair);

    // A class, not a record: the sweep removes an entry only while the dictionary still holds THIS instance, and a
    // record would compare by content.
    private sealed class Entry(string dependencyFingerprint, AIAgent agent, long lastUsed, long sequence)
    {
        private long _lastUsed = lastUsed;

        public string DependencyFingerprint { get; } = dependencyFingerprint;

        public AIAgent Agent { get; } = agent;

        public long Sequence { get; } = sequence;

        public long LastUsed => Volatile.Read(ref _lastUsed);

        public void Touch(long now) => Volatile.Write(ref _lastUsed, now);
    }
}
