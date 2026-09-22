using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Caches compiled agents keyed by
/// <c>(tenant, name, definition fingerprint, dependency fingerprint, culture)</c>.
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
    private readonly ConcurrentDictionary<CacheKey, AIAgent> _entries = new();

    /// <summary>Number of entries in the cache.</summary>
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
    /// Current fingerprint of the definition's <em>external</em> dependencies:
    /// skills and callable sub-agents. These can change without incrementing
    /// the definition's own version, so they need a separate key component.
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

        var key = new CacheKey(tenantId, name, definitionFingerprint, dependencyFingerprint, culture);

        // 🚨 Checked separately from the GetOrAdd(key, valueFactory) call below:
        // that overload allocates its `Func<CacheKey, AIAgent>` closure argument
        // BEFORE it even looks at the dictionary, on every call - including a
        // cache HIT, which is the overwhelmingly common case once a definition
        // is compiled once. TryGetValue first keeps the hit path allocation-free,
        // matching the async overload below, which was already written this way.
        if (_entries.TryGetValue(key, out var existing))
        {
            return existing;
        }

        // GetOrAdd(key, valueFactory) can run the factory more than once for the
        // same key. Agent production is side-effect free, so this is not a
        // problem; any extra-produced instance is discarded.
        return _entries.GetOrAdd(key, _ => factory());
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

        var key = new CacheKey(tenantId, name, definitionFingerprint, dependencyFingerprint, culture);

        if (_entries.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var produced = await factory().ConfigureAwait(false);

        return _entries.GetOrAdd(key, produced);
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

    private readonly record struct CacheKey(string TenantId, string Name, string DefinitionFingerprint, string DependencyFingerprint, string Culture);
}
