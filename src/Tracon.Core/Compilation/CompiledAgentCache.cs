using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Caches compiled agents keyed by
/// <c>(tenant, name, definition fingerprint, dependency fingerprint, culture)</c>,
/// holding one entry per <c>(tenant, name, definition fingerprint, culture)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The key measures CONTENT, not a version number.</strong> An earlier
/// design keyed the entry by the definition's version and relied on "a save
/// increments the version, so the entry naturally goes stale". That reasoning
/// does not hold for a DELETE followed by a CREATE of the same name: the store
/// restarts numbering, the new definition is version <c>1</c> again, and the
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
/// <strong>The cache is bounded by definitions, not by dependency changes.</strong>
/// The dependency fingerprint changes without any operator action: a remote MCP
/// server that edits a tool's description or schema changes it at the next refresh.
/// Each <c>(tenant, name, definition fingerprint, culture)</c> therefore holds ONE
/// entry, and a compile under a new dependency fingerprint replaces it; the replaced
/// agent is never read again. The definition fingerprint stays a separate slot: two
/// versions of one agent served side by side (an experiment's variants) must not
/// evict each other on every run. A new definition version still adds a slot, which
/// only an operator's save can produce.
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
    private readonly ConcurrentDictionary<SlotKey, Entry> _entries = new();

    internal CompiledAgentCache()
    {
    }

    /// <summary>Number of entries in the cache: one per tenant, agent name, definition, and culture.</summary>
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
    public void Clear() => _entries.Clear();

    // F-308: without the slot, a remote tool schema that changed on every refresh
    // would have added an entry every five minutes, and none was ever removed.
    /// <summary>
    /// Stores a freshly compiled agent in its slot, replacing an entry compiled under
    /// another dependency fingerprint.
    /// </summary>
    /// <returns>
    /// The agent the slot holds for <paramref name="dependencyFingerprint"/> afterwards:
    /// <paramref name="produced"/>, or the instance a concurrent caller stored first.
    /// </returns>
    private AIAgent Store(SlotKey key, string dependencyFingerprint, AIAgent produced)
    {
        var candidate = new Entry(dependencyFingerprint, produced);

        while (true)
        {
            if (_entries.TryGetValue(key, out var current))
            {
                if (string.Equals(current.DependencyFingerprint, dependencyFingerprint, StringComparison.Ordinal))
                {
                    return current.Agent;
                }

                if (_entries.TryUpdate(key, candidate, current))
                {
                    return produced;
                }
            }
            else if (_entries.TryAdd(key, candidate))
            {
                return produced;
            }
        }
    }

    private readonly record struct SlotKey(string TenantId, string Name, string DefinitionFingerprint, string Culture);

    private sealed record Entry(string DependencyFingerprint, AIAgent Agent);
}
