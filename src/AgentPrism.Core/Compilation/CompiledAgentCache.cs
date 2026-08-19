using System.Collections.Concurrent;
using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Caches compiled agents keyed by <c>(tenant, name, version, skill fingerprint, culture)</c>.
/// </summary>
/// <remarks>
/// <para>
/// When a definition is updated, its version increases and the cache
/// <em>naturally</em> becomes stale. There is therefore no explicit
/// invalidation logic; the old version's entry is cleared with <see cref="Evict"/>.
/// </para>
/// <para>
/// 🚨 The <c>tenant</c> component of the key is REQUIRED. Agent names are unique
/// only within a tenant (see <c>SqlAgentDefinitionStore</c>) - it is common for
/// two different tenants to have a definition with the same name (e.g.
/// <c>"support"</c>), the same version (the first record is always <c>1</c>),
/// and the same dependency fingerprint (an empty string for both if they use
/// no skills/callable agents). If the tenant is not included in the key,
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
    /// <param name="version">Definition version.</param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public AIAgent GetOrAdd(string tenantId, string name, int version, Func<AIAgent> factory)
        => GetOrAdd(tenantId, name, version, string.Empty, string.Empty, factory);

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency
    /// fingerprint; if absent, produces it with <paramref name="factory"/>
    /// and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="version">Definition version.</param>
    /// <param name="dependencyFingerprint">
    /// Current fingerprint of the definition's <em>external</em> dependencies:
    /// skills and callable sub-agents. These can change without incrementing
    /// the definition's own version, so they need a separate key component.
    /// </param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    public AIAgent GetOrAdd(string tenantId, string name, int version, string dependencyFingerprint, Func<AIAgent> factory)
        => GetOrAdd(tenantId, name, version, dependencyFingerprint, string.Empty, factory);

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency fingerprint and
    /// requested culture; if absent, produces it with <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="version">Definition version.</param>
    /// <param name="dependencyFingerprint">
    /// See <see cref="GetOrAdd(string, string, int, string, Func{AIAgent})"/>.
    /// </param>
    /// <param name="culture">
    /// The culture the definition was compiled with (see <see cref="InstructionCultureResolver"/>).
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
        int version,
        string dependencyFingerprint,
        string culture,
        Func<AIAgent> factory)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dependencyFingerprint);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(factory);

        // GetOrAdd(key, valueFactory) can run the factory more than once for the
        // same key. Agent production is side-effect free, so this is not a
        // problem; any extra-produced instance is discarded.
        return _entries.GetOrAdd(new CacheKey(tenantId, name, version, dependencyFingerprint, culture), _ => factory());
    }

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency
    /// fingerprint; if absent, produces it asynchronously with
    /// <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="version">Definition version.</param>
    /// <param name="dependencyFingerprint">See <see cref="GetOrAdd(string, string, int, string, Func{AIAgent})"/>.</param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    /// <remarks>
    /// Phase 65 (BYOK): compiling now needs an async credential lookup on a
    /// cache miss. <see cref="ConcurrentDictionary{TKey,TValue}"/> has no
    /// native async <c>GetOrAdd</c>; the same "the factory may run more than
    /// once, production is side-effect free, an extra instance is discarded"
    /// guarantee as the sync overload applies here too.
    /// </remarks>
    public ValueTask<AIAgent> GetOrAddAsync(
        string tenantId,
        string name,
        int version,
        string dependencyFingerprint,
        Func<ValueTask<AIAgent>> factory)
        => GetOrAddAsync(tenantId, name, version, dependencyFingerprint, string.Empty, factory);

    /// <summary>
    /// Retrieves the agent from the cache together with its dependency fingerprint and
    /// requested culture; if absent, produces it asynchronously with
    /// <paramref name="factory"/> and adds it.
    /// </summary>
    /// <param name="tenantId">Identifier of the tenant requesting resolution.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="version">Definition version.</param>
    /// <param name="dependencyFingerprint">See <see cref="GetOrAdd(string, string, int, string, Func{AIAgent})"/>.</param>
    /// <param name="culture">See <see cref="GetOrAdd(string, string, int, string, string, Func{AIAgent})"/>.</param>
    /// <param name="factory">Producer called when absent from the cache.</param>
    /// <returns>The compiled agent.</returns>
    public async ValueTask<AIAgent> GetOrAddAsync(
        string tenantId,
        string name,
        int version,
        string dependencyFingerprint,
        string culture,
        Func<ValueTask<AIAgent>> factory)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dependencyFingerprint);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(factory);

        var key = new CacheKey(tenantId, name, version, dependencyFingerprint, culture);

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

    /// <summary>Removes all versions of an agent from the cache.</summary>
    /// <param name="name">Agent name.</param>
    public void Evict(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var key in _entries.Keys)
        {
            if (string.Equals(key.Name, name, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Empties the cache entirely.</summary>
    public void Clear() => _entries.Clear();

    private readonly record struct CacheKey(string TenantId, string Name, int Version, string DependencyFingerprint, string Culture);
}
