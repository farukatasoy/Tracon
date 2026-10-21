using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.VectorData;

namespace Tracon.Tests.Common;

/// <summary>
/// The smallest <see cref="VectorStore"/> that behaves like a real one for the records of conversation
/// memory: it keeps the dictionary records, applies a filter by RUNNING the expression a caller builds,
/// and ranks a search by the words the query and a record share.
/// </summary>
/// <remarks>
/// <para>
/// A filter is executed, not skipped, so a test that scopes a search by tenant, agent, user and
/// session proves the expression the framework built selects what the test says it selects. What this
/// store cannot prove is a real connector's translation of the expression: that is the connector's own
/// contract, and the site tells a host to check it.
/// </para>
/// <para>
/// A test makes the store fail with <see cref="FailOn"/>.
/// </para>
/// </remarks>
internal sealed class InMemoryVectorStore : VectorStore
{
    // A linked project also builds for net8.0, which has no System.Threading.Lock.
#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly Dictionary<string, InMemoryCollection> _collections = new(StringComparer.Ordinal);

    /// <summary>Gets the definition the first <see cref="GetDynamicCollection"/> call handed over.</summary>
    public VectorStoreCollectionDefinition? Definition { get; private set; }

    /// <summary>Gets or sets which operation throws. <see langword="null"/> when none does.</summary>
    public Func<string, Exception?>? FailOn { get; set; }

    /// <summary>Gets or sets whether a delete reports success and removes nothing, as a store with a stale read would.</summary>
    public bool IgnoreDeletes { get; set; }

    /// <summary>Gets the records of a collection, a snapshot.</summary>
    /// <param name="name">The collection name.</param>
    /// <returns>The records; empty when the collection does not exist.</returns>
    public IReadOnlyList<Dictionary<string, object?>> Records(string name)
    {
        lock (_gate)
        {
            return _collections.TryGetValue(name, out var collection) ? collection.Snapshot() : [];
        }
    }

    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(string name, VectorStoreCollectionDefinition definition)
    {
        lock (_gate)
        {
            Definition ??= definition;

            if (!_collections.TryGetValue(name, out var collection))
            {
                _collections[name] = collection = new InMemoryCollection(name, this);
            }

            return collection;
        }
    }

    /// <inheritdoc />
    [RequiresDynamicCode("The typed collection of the base class needs dynamic code; conversation memory uses the dynamic collection.")]
    [RequiresUnreferencedCode("The typed collection of the base class needs unreferenced code; conversation memory uses the dynamic collection.")]
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(string name, VectorStoreCollectionDefinition? definition = null)
        => throw new NotSupportedException("Conversation memory uses a dynamic collection.");

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string[] names;

        lock (_gate)
        {
            names = [.. _collections.Keys];
        }

        foreach (var name in names)
        {
            yield return name;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_collections.TryGetValue(name, out var collection) && collection.Exists);
        }
    }

    /// <inheritdoc />
    public override Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _collections.Remove(name);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) => null;

    private void ThrowIfFailing(string operation)
    {
        if (FailOn?.Invoke(operation) is { } failure)
        {
            throw failure;
        }
    }

    private sealed class InMemoryCollection(string name, InMemoryVectorStore owner) : VectorStoreCollection<object, Dictionary<string, object?>>
    {
#if NET9_0_OR_GREATER
        private readonly Lock _gate = new();
#else
        private readonly object _gate = new();
#endif
        private readonly List<Dictionary<string, object?>> _records = [];

        public bool Exists { get; private set; }

        public override string Name => name;

        public List<Dictionary<string, object?>> Snapshot()
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }

        public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Exists);

        public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
        {
            Exists = true;

            return Task.CompletedTask;
        }

        public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _records.Clear();
            }

            Exists = false;

            return Task.CompletedTask;
        }

        public override Task<Dictionary<string, object?>?> GetAsync(object key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_records.FirstOrDefault(record => Equals(record["Key"], key)));
            }
        }

        public override async IAsyncEnumerable<Dictionary<string, object?>> GetAsync(
            Expression<Func<Dictionary<string, object?>, bool>> filter,
            int top,
            FilteredRecordRetrievalOptions<Dictionary<string, object?>>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.ThrowIfFailing("get");

            var matches = filter.Compile();

            List<Dictionary<string, object?>> page;

            lock (_gate)
            {
                page = [.. _records.Where(matches).Skip(options?.Skip ?? 0).Take(top)];
            }

            foreach (var record in page)
            {
                yield return record;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public override Task DeleteAsync(object key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.ThrowIfFailing("delete");

            if (owner.IgnoreDeletes)
            {
                return Task.CompletedTask;
            }

            lock (_gate)
            {
                _records.RemoveAll(record => Equals(record["Key"], key));
            }

            return Task.CompletedTask;
        }

        public override Task DeleteAsync(IEnumerable<object> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.ThrowIfFailing("delete");

            if (owner.IgnoreDeletes)
            {
                return Task.CompletedTask;
            }

            var set = keys.ToHashSet();

            lock (_gate)
            {
                _records.RemoveAll(record => set.Contains(record["Key"]!));
            }

            return Task.CompletedTask;
        }

        public override Task UpsertAsync(Dictionary<string, object?> record, CancellationToken cancellationToken = default)
            => UpsertAsync([record], cancellationToken);

        public override Task UpsertAsync(IEnumerable<Dictionary<string, object?>> records, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.ThrowIfFailing("upsert");

            lock (_gate)
            {
                foreach (var record in records)
                {
                    _records.RemoveAll(existing => Equals(existing["Key"], record["Key"]));
                    _records.Add(new Dictionary<string, object?>(record, StringComparer.Ordinal));
                }
            }

            return Task.CompletedTask;
        }

        public override async IAsyncEnumerable<VectorSearchResult<Dictionary<string, object?>>> SearchAsync<TInput>(
            TInput searchValue,
            int top,
            VectorSearchOptions<Dictionary<string, object?>>? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.ThrowIfFailing("search");

            var query = Words(searchValue as string);
            var matches = options?.Filter is { } filter ? filter.Compile() : static _ => true;

            List<VectorSearchResult<Dictionary<string, object?>>> found;

            lock (_gate)
            {
                found =
                [
                    .. _records
                        .Where(matches)
                        .Select(record => (Record: record, Score: Words(record["Content"] as string).Intersect(query, StringComparer.OrdinalIgnoreCase).Count()))
                        .Where(static scored => scored.Score > 0)
                        .OrderByDescending(static scored => scored.Score)
                        .Take(top)
                        .Select(static scored => new VectorSearchResult<Dictionary<string, object?>>(scored.Record, scored.Score)),
                ];
            }

            foreach (var result in found)
            {
                yield return result;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        public override object? GetService(Type serviceType, object? serviceKey = null) => null;

        private static HashSet<string> Words(string? text)
            => text is null
                ? []
                : text.Split([' ', '.', ',', '?', '!', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(static word => word.Length > 2)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
