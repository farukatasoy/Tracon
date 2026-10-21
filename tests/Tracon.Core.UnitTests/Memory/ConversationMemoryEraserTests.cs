using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Memory;

/// <summary>
/// What the eraser counts, exports and deletes: the records of the sessions it is told about, in the
/// tenant it is told about, and nothing else.
/// </summary>
public sealed class ConversationMemoryEraserTests : IDisposable
{
    private const string Collection = "memory";

    private readonly InMemoryVectorStore _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task It_deletes_the_records_of_the_named_session_and_leaves_the_other_sessions()
    {
        await SeedAsync("tenant-a", "session-1", "agent-a", "ada", "one", "two");
        await SeedAsync("tenant-a", "session-2", "agent-a", "ada", "three");

        var deleted = await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None);

        deleted.ShouldBe(2);
        Contents().ShouldBe(["three"]);
    }

    [Fact]
    public async Task It_never_deletes_the_records_of_another_tenant_that_carry_the_same_session_id()
    {
        // 🚨 A session id is unique inside one tenant only.
        await SeedAsync("tenant-a", "shared-session", "agent-a", "ada", "a-record");
        await SeedAsync("tenant-b", "shared-session", "agent-a", "ada", "b-record");

        await Eraser().EraseAsync("tenant-a", ["shared-session"], CancellationToken.None);

        Contents().ShouldBe(["b-record"]);
    }

    [Fact]
    public async Task It_deletes_the_records_of_every_agent_and_user_the_session_holds()
    {
        await SeedAsync("tenant-a", "session-1", "agent-a", "ada", "from-a");
        await SeedAsync("tenant-a", "session-1", "agent-b", "grace", "from-b");

        var deleted = await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None);

        deleted.ShouldBe(2);
        Contents().ShouldBeEmpty();
    }

    [Fact]
    public async Task It_deletes_more_records_than_one_page_holds()
    {
        for (var index = 0; index < 450; index++)
        {
            await SeedAsync("tenant-a", "session-1", "agent-a", "ada", $"record-{index}");
        }

        await SeedAsync("tenant-a", "session-2", "agent-a", "ada", "keep");

        var deleted = await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None);

        deleted.ShouldBe(450);
        Contents().ShouldBe(["keep"]);
    }

    [Fact]
    public async Task A_count_deletes_nothing_and_reads_more_than_one_page()
    {
        for (var index = 0; index < 450; index++)
        {
            await SeedAsync("tenant-a", "session-1", "agent-a", "ada", $"record-{index}");
        }

        var count = await Eraser().CountAsync("tenant-a", ["session-1"], CancellationToken.None);

        count.ShouldBe(450);
        Contents().Count.ShouldBe(450);
    }

    [Fact]
    public async Task An_export_lists_the_records_with_their_text_and_leaves_out_the_vector_and_the_user()
    {
        await SeedAsync("tenant-a", "session-1", "agent-a", "ada", "My favourite colour is teal.");
        await SeedAsync("tenant-a", "session-2", "agent-a", "ada", "another session");

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            await Eraser().WriteExportAsync("tenant-a", ["session-1"], writer, CancellationToken.None);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        var records = document.RootElement.GetProperty("conversation_memory");

        var record = records.EnumerateArray().ShouldHaveSingleItem();
        record.GetProperty("content").GetString().ShouldBe("My favourite colour is teal.");
        record.GetProperty("sessionId").GetString().ShouldBe("session-1");
        record.GetProperty("agent").GetString().ShouldBe("agent-a");
        record.TryGetProperty("contentEmbedding", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task With_no_vector_store_registered_there_is_nothing_to_count_export_or_erase()
    {
        var eraser = new ConversationMemoryEraser(new ServiceCollection().BuildServiceProvider());

        (await eraser.CountAsync("tenant-a", ["session-1"], CancellationToken.None)).ShouldBe(0);
        (await eraser.EraseAsync("tenant-a", ["session-1"], CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task With_no_collection_yet_there_is_nothing_to_count_or_erase()
    {
        (await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None)).ShouldBe(0);
        (await Eraser().CountAsync("tenant-a", ["session-1"], CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task A_store_that_fails_to_delete_fails_the_erasure_and_the_records_stay()
    {
        await SeedAsync("tenant-a", "session-1", "agent-a", "ada", "one");
        _store.FailOn = static operation => string.Equals(operation, "delete", StringComparison.Ordinal)
            ? new InvalidOperationException("store is down")
            : null;

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None));

        _store.FailOn = null;
        Contents().ShouldBe(["one"]);

        // A repeat call finishes what the first could not.
        (await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public async Task A_store_that_reports_a_delete_and_keeps_the_record_stops_the_erasure_instead_of_looping()
    {
        await SeedAsync("tenant-a", "session-1", "agent-a", "ada", "one");
        _store.IgnoreDeletes = true;

        var failure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await Eraser().EraseAsync("tenant-a", ["session-1"], CancellationToken.None));

        failure.Message.ShouldContain("may still exist");
    }

    [Fact]
    public async Task A_cancelled_erasure_passes_the_cancellation_on()
    {
        await SeedAsync("tenant-a", "session-1", "agent-a", "ada", "one");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _store.FailOn = static _ => null;

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Eraser().EraseAsync("tenant-a", ["session-1"], cancelled.Token));
    }

    private ConversationMemoryEraser Eraser()
        => new(new ServiceCollection()
            .AddSingleton<VectorStore>(_store)
            .AddOptions<TraconConversationMemoryOptions>().Configure(static options =>
            {
                options.CollectionName = Collection;
                options.VectorDimensions = 8;
            }).Services
            .BuildServiceProvider());

    private List<string?> Contents() => [.. _store.Records(Collection).Select(static record => (string?)record["Content"])];

    private async Task SeedAsync(string tenant, string session, string agent, string user, params string[] contents)
    {
        using var collection = _store.GetDynamicCollection(Collection, ConversationMemorySchema.CreateDefinition(8));
        await collection.EnsureCollectionExistsAsync();

        foreach (var content in contents)
        {
            await collection.UpsertAsync(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Key"] = Guid.NewGuid(),
                ["Role"] = "user",
                ["MessageId"] = null,
                ["AuthorName"] = null,
                ["ApplicationId"] = tenant,
                ["AgentId"] = agent,
                ["UserId"] = user,
                ["SessionId"] = session,
                ["Content"] = content,
                ["CreatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["ContentEmbedding"] = content,
            });
        }
    }
}
