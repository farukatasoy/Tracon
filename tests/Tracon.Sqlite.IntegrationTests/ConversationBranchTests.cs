using System.Globalization;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Store behavior of conversation branching (Phase 47).
/// </summary>
/// <remarks>
/// SQLite was chosen because it is the cheapest of the three SQL providers to
/// run, and the queries live in the shared layer (<c>SqlConversationBranchStore</c>).
/// The tenant filter and the <c>INSERT … SELECT</c> logic are dialect-independent.
/// </remarks>
public sealed class ConversationBranchTests(SqliteFixture fixture)
{
    private const string Tenant = "default";

    /// <summary>An empty JSON object. Avoids brace-escaping inside raw SQL.</summary>
    private const string EmptyJsonObject = "{}";

    [Fact]
    public async Task Branch_copies_items_up_to_the_given_sequence()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 4);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 1);

        branch.ShouldNotBeNull();
        branch!.Value.CopiedItemCount.ShouldBe(2);
        branch.Value.BranchFromSequence.ShouldBe(1);

        var copied = await CountItemsAsync(context, branch.Value.ConversationId);

        copied.ShouldBe(2);
    }

    [Fact]
    public async Task Branch_without_a_limit_copies_the_ENTIRE_conversation()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 3);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        branch!.Value.CopiedItemCount.ShouldBe(3);
        branch.Value.BranchFromSequence.ShouldBe(2);
    }

    [Fact]
    public async Task Writing_to_the_branch_does_not_change_the_PARENT_conversation()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 3);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 1);

        // Write a new item to the branch.
        await InsertItemAsync(context, branch!.Value.ConversationId, sequence: 2, text: "new-in-branch");

        // 🚨 This is the entire value of the copy design: the two conversations
        // are INDEPENDENT of each other. If it used a pointer chain instead,
        // writing to the branch would also change reads of the parent conversation.
        (await CountItemsAsync(context, parent)).ShouldBe(3);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(3);
    }

    [Fact]
    public async Task An_empty_limit_is_valid_but_opens_an_empty_branch()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        // seq = -1: no item is copied, but the conversation row is opened. An
        // empty branch is valid — the user may want to restart the
        // conversation from scratch.
        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: -1);

        branch!.Value.CopiedItemCount.ShouldBe(0);
        branch.Value.BranchFromSequence.ShouldBe(-1);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task Branch_pointer_and_source_metadata_are_copied()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2, agentName: "assistant");

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 0);

        var agentName = await context.ScalarAsync<string>(
            $"SELECT agent_name FROM {context.TablePrefix}conversations WHERE id = '{Sql(branch!.Value.ConversationId)}';");
        var parentPointer = await context.ScalarAsync<string>(
            $"SELECT parent_conversation_id FROM {context.TablePrefix}conversations WHERE id = '{Sql(branch.Value.ConversationId)}';");
        var branchFrom = await context.ScalarAsync<long>(
            $"SELECT branch_from_seq FROM {context.TablePrefix}conversations WHERE id = '{Sql(branch.Value.ConversationId)}';");

        agentName.ShouldBe("assistant");
        parentPointer.ShouldNotBeNull();
        Guid.Parse(parentPointer!, CultureInfo.InvariantCulture).ShouldBe(parent);
        branchFrom.ShouldBe(0);
    }

    [Fact]
    public async Task Branch_SURVIVES_when_the_parent_conversation_is_deleted()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        await context.ExecuteAsync($"DELETE FROM {context.TablePrefix}conversations WHERE id = '{Sql(parent)}';");

        // The branch keeps living and its items stay WITH IT. The pointer now
        // refers to an origin that can no longer be resolved; no read path
        // joins against it.
        (await CountItemsAsync(context, branch!.Value.ConversationId)).ShouldBe(2);
    }

    [Fact]
    public async Task Another_tenants_conversation_cannot_be_branched()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        (await context.ConversationBranches.BranchAsync("other-tenant", parent, upToSequence: null))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_nonexistent_conversation_returns_null()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        (await context.ConversationBranches.BranchAsync(Tenant, TraconId.NewId(), upToSequence: null))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_conversation_with_a_thousand_items_can_be_branched()
    {
        // Open Question 4 asked that the cost of copying a large conversation
        // be MEASURED. The copy writes row by row in a single transaction
        // (the uuid v7 id is generated in the application, see
        // SqlConversationBranchStore); this test confirms that path works and
        // counts correctly even at a thousand items.
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 1_000);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        branch!.Value.CopiedItemCount.ShouldBe(1_000);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(1_000);
    }

    private static async ValueTask<Guid> SeedConversationAsync(
        SqliteTestContext context,
        int itemCount,
        string agentName = "test-agent")
    {
        var conversationId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync(
            $"""
            INSERT INTO {context.TablePrefix}conversations (id, tenant_id, agent_name, metadata, created_at, updated_at)
            VALUES ('{Sql(conversationId)}', '{Tenant}', '{agentName}', '{EmptyJsonObject}', '{now}', '{now}');
            """);

        for (var index = 0; index < itemCount; index++)
        {
            await InsertItemAsync(context, conversationId, index, $"message-{index}");
        }

        return conversationId;
    }

    private static async ValueTask InsertItemAsync(
        SqliteTestContext context,
        Guid conversationId,
        long sequence,
        string text)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // The content mimics a polymorphic ChatMessage: the `$type`
        // discriminator is the FIRST property and must be carried as-is
        // during the copy (K-027).
        await context.ExecuteAsync(
            $$"""
            INSERT INTO {{context.TablePrefix}}conversation_items (id, conversation_id, seq, item, created_at)
            VALUES ('{{Sql(TraconId.NewId())}}', '{{Sql(conversationId)}}', {{sequence}},
                    '{"$type":"text","text":"{{text}}"}', '{{now}}');
            """);
    }

    private static async ValueTask<long> CountItemsAsync(SqliteTestContext context, Guid conversationId)
        => await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.TablePrefix}conversation_items WHERE conversation_id = '{Sql(conversationId)}';");

    /// <summary>
    /// Converts an id to the form SQLite STORES it in.
    /// </summary>
    /// <remarks>
    /// 🚨 <c>Microsoft.Data.Sqlite</c> writes <see cref="Guid"/> values as
    /// UPPERCASE, hyphenated text (K-191), and SQLite text comparison is
    /// case-sensitive. If hand-written SQL uses a lowercase id, <c>WHERE</c>
    /// SILENTLY finds no rows — this test file failed exactly this way on its
    /// first run.
    /// </remarks>
    private static string Sql(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();
}
