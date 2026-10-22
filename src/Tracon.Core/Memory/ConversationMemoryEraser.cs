using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;

namespace Tracon;

/// <summary>
/// Counts, exports and deletes the conversation memory records of sessions: the copy of a session's
/// messages that lives in the host's vector store.
/// </summary>
/// <remarks>
/// <para>
/// It reads the store and the collection settings from the container when it first has work, not
/// when it is built: it is constructed with the SQL stores, before a host that registers its
/// <c>VectorStore</c> late has done so. A host with no store has no records, so every method
/// then answers "nothing" and does not fail.
/// </para>
/// <para>
/// Every filter names the tenant as well as the session. A session id is unique inside one tenant
/// only; filtering by the session alone would delete another tenant's records that carry the same id.
/// </para>
/// </remarks>
internal sealed class ConversationMemoryEraser(IServiceProvider services) : ISubjectContentEraser
{
    /// <summary>How many records one page reads and one delete removes.</summary>
    private const int PageSize = 200;

    /// <inheritdoc />
    public string Target => "conversation_memory";

    /// <inheritdoc />
    public async ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        tenantId = Canonical(tenantId);
        var count = 0;

        await ForEachSessionAsync(
            tenantId,
            sessionIds,
            async (collection, sessionId, token) =>
            {
                await foreach (var _ in ReadAsync(collection, ConversationMemorySchema.ForSession(tenantId, sessionId), token).ConfigureAwait(false))
                {
                    count++;
                }
            },
            cancellationToken).ConfigureAwait(false);

        return count;
    }

    /// <inheritdoc />
    public async ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
    {
        tenantId = Canonical(tenantId);
        var deleted = 0;

        await ForEachSessionAsync(
            tenantId,
            sessionIds,
            async (collection, sessionId, token) => deleted += await EraseSessionAsync(collection, tenantId, sessionId, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return deleted;
    }

    /// <inheritdoc />
    public async ValueTask WriteExportAsync(
        string tenantId,
        IReadOnlyList<string> sessionIds,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        tenantId = Canonical(tenantId);

        writer.WriteStartArray(Target);

        await ForEachSessionAsync(
            tenantId,
            sessionIds,
            async (collection, sessionId, token) =>
            {
                await foreach (var record in ReadAsync(collection, ConversationMemorySchema.ForSession(tenantId, sessionId), token).ConfigureAwait(false))
                {
                    writer.WriteStartObject();
                    WriteField(writer, "sessionId", record, ConversationMemorySchema.SessionField);
                    WriteField(writer, "agent", record, ConversationMemorySchema.AgentField);
                    WriteField(writer, "role", record, ConversationMemorySchema.RoleField);
                    WriteField(writer, "content", record, ConversationMemorySchema.ContentField);
                    WriteField(writer, "createdAt", record, ConversationMemorySchema.CreatedAtField);
                    writer.WriteEndObject();
                }
            },
            cancellationToken).ConfigureAwait(false);

        writer.WriteEndArray();
    }

    /// <summary>
    /// The tenant in the form the records were written in: the provider writes <c>AmbientTenantScope.Normalize</c>,
    /// and a tenant context that does not normalize must still find them.
    /// </summary>
    private static string Canonical(string tenantId) => AmbientTenantScope.Normalize(tenantId);

    private static void WriteField(Utf8JsonWriter writer, string name, Dictionary<string, object?> record, string field)
        => writer.WriteString(name, record.TryGetValue(field, out var value) ? value as string : null);

    /// <summary>Deletes the records of one session, a page at a time, until the filter finds none.</summary>
    /// <exception cref="InvalidOperationException">The store returned a record again after it was told to delete it.</exception>
    private static async ValueTask<int> EraseSessionAsync(
        VectorStoreCollection<object, Dictionary<string, object?>> collection,
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var filter = ConversationMemorySchema.ForSession(tenantId, sessionId);
        var deleted = 0;
        object? previousFirstKey = null;

        while (true)
        {
            var keys = new List<object>(PageSize);

            await foreach (var record in collection.GetAsync(filter, PageSize, options: null, cancellationToken).ConfigureAwait(false))
            {
                keys.Add(record[ConversationMemorySchema.KeyField]!);
            }

            if (keys.Count == 0)
            {
                return deleted;
            }

            // A store that reports a delete and still returns the record would otherwise loop forever.
            if (previousFirstKey is not null && Equals(previousFirstKey, keys[0]))
            {
                throw new InvalidOperationException(
                    "The vector store returned a conversation memory record again after Tracon deleted it. " +
                    "The erasure stopped; the records of the session may still exist.");
            }

            await collection.DeleteAsync(keys, cancellationToken).ConfigureAwait(false);

            deleted += keys.Count;
            previousFirstKey = keys[0];
        }
    }

    /// <summary>Reads every record the filter finds, a page at a time.</summary>
    private static async IAsyncEnumerable<Dictionary<string, object?>> ReadAsync(
        VectorStoreCollection<object, Dictionary<string, object?>> collection,
        Expression<Func<Dictionary<string, object?>, bool>> filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var skip = 0; ; skip += PageSize)
        {
            var read = 0;

            await foreach (var record in collection
                .GetAsync(filter, PageSize, new FilteredRecordRetrievalOptions<Dictionary<string, object?>>
                {
                    Skip = skip,

                    // A page needs a stable order: without one, a connector may repeat or skip a record between pages.
                    OrderBy = static order => order.Ascending(static record => record[ConversationMemorySchema.KeyField]),
                }, cancellationToken)
                .ConfigureAwait(false))
            {
                read++;

                yield return record;
            }

            if (read < PageSize)
            {
                yield break;
            }
        }
    }

    /// <summary>Opens the memory collection and runs <paramref name="action"/> for each session; does nothing when there is no store or no collection.</summary>
    private async ValueTask ForEachSessionAsync(
        string tenantId,
        IReadOnlyList<string> sessionIds,
        Func<VectorStoreCollection<object, Dictionary<string, object?>>, string, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (sessionIds.Count == 0
            || services.GetService<VectorStore>() is not { } store
            || services.GetService<IOptions<TraconConversationMemoryOptions>>()?.Value is not { } options)
        {
            return;
        }

        using var collection = store.GetDynamicCollection(
            options.CollectionName,
            ConversationMemorySchema.CreateDefinition(options.VectorDimensions));

        if (!await collection.CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        foreach (var sessionId in sessionIds)
        {
            await action(collection, sessionId, cancellationToken).ConfigureAwait(false);
        }
    }
}
