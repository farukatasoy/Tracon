using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Agent tanimlarini surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <para>
/// Bu bir test yardimcisi <em>degildir</em>. AgentPrism'in veritabani olmadan
/// calisabilmesini saglayan birinci sinif bir uygulamadir; boylece paketi kuran
/// bir gelistirici hicbir altyapi kurmadan calisan bir kontrol duzlemi gorur.
/// </para>
/// <para>
/// <strong>Sinirlari:</strong> veriler surec omruyle sinirlidir ve birden cok
/// dugum arasinda paylasilmaz. Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </para>
/// </remarks>
public sealed class InMemoryAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly ConcurrentDictionary<string, List<AgentDefinition>> _versions =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(name, out var history))
        {
            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }

        lock (history)
        {
            return new ValueTask<AgentDefinition?>(history.Count == 0 ? null : history[^1]);
        }
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(name, out var history))
        {
            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }

        lock (history)
        {
            foreach (var candidate in history)
            {
                if (candidate.Version == version)
                {
                    return new ValueTask<AgentDefinition?>(candidate);
                }
            }

            return new ValueTask<AgentDefinition?>((AgentDefinition?)null);
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var current = new List<AgentDefinition>(_versions.Count);

        foreach (var history in _versions.Values)
        {
            lock (history)
            {
                if (history.Count > 0)
                {
                    current.Add(history[^1]);
                }
            }
        }

        current.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return new ValueTask<IReadOnlyList<AgentDefinition>>(current);
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var history = _versions.GetOrAdd(definition.Name, static _ => []);

        lock (history)
        {
            var saved = definition with
            {
                Origin = AgentDefinitionOrigin.Database,
                Version = history.Count == 0 ? 1 : history[^1].Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            history.Add(saved);
            return new ValueTask<AgentDefinition>(saved);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new ValueTask<bool>(_versions.TryRemove(name, out _));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(name, out var history))
        {
            return new ValueTask<IReadOnlyList<AgentDefinition>>(Array.Empty<AgentDefinition>());
        }

        lock (history)
        {
            var snapshot = new List<AgentDefinition>(history);
            snapshot.Reverse();
            return new ValueTask<IReadOnlyList<AgentDefinition>>(snapshot);
        }
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_versions.TryGetValue(name, out var history))
        {
            throw new AgentPrismException($"'{name}' adinda bir agent tanimi bulunamadi.");
        }

        lock (history)
        {
            AgentDefinition? target = null;

            foreach (var candidate in history)
            {
                if (candidate.Version == version)
                {
                    target = candidate;
                    break;
                }
            }

            if (target is null)
            {
                throw new AgentPrismException($"'{name}' agent'inin {version} numarali surumu bulunamadi.");
            }

            // Geri alma eski surumu silmez; icerigini yeni bir surum olarak kaydeder.
            var restored = target with
            {
                Version = history[^1].Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            history.Add(restored);
            return new ValueTask<AgentDefinition>(restored);
        }
    }
}
