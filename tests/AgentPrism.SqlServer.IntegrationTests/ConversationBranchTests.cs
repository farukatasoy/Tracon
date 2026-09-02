using System.Globalization;
using AgentPrism.SqlServer.IntegrationTests.Infrastructure;

namespace AgentPrism.SqlServer.IntegrationTests;

/// <summary>
/// Conversation branching (Phase 47) against a real SQL Server database.
/// </summary>
/// <remarks>
/// <para>
/// Branching was only ever covered on SQLite, on the reasoning that the queries
/// live in the shared layer and are dialect-independent. That reasoning held for
/// the SQL <em>text</em> and missed the READER: a shared query's result columns
/// do not have the same CLR type on every provider. <c>COUNT(*)</c> is
/// <c>bigint</c> on PostgreSQL and SQLite but <c>int</c> on SQL Server, so
/// <c>SqlDataReader.GetInt64</c> threw <c>InvalidCastException</c> and branching
/// was broken on SQL Server for as long as the feature had existed.
/// </para>
/// <para>
/// A behavior that crosses the provider boundary needs a run on that provider;
/// one dialect passing proves the text, not the round trip.
/// </para>
/// </remarks>
public sealed class ConversationBranchTests(SqlServerFixture fixture)
{
    private const string Tenant = "default";

    /// <summary>An empty JSON object. Avoids brace-escaping inside raw SQL.</summary>
    private const string EmptyJsonObject = "{}";

    [Fact]
    public async Task Branch_copies_items_up_to_the_given_sequence()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 4);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: 1);

        branch.ShouldNotBeNull();
        branch!.Value.CopiedItemCount.ShouldBe(2);
        branch.Value.BranchFromSequence.ShouldBe(1);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(2);
    }

    [Fact]
    public async Task Branch_without_a_limit_copies_the_ENTIRE_conversation()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 3);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: null);

        branch!.Value.CopiedItemCount.ShouldBe(3);
        branch.Value.BranchFromSequence.ShouldBe(2);
    }

    [Fact]
    public async Task An_empty_limit_is_valid_but_opens_an_empty_branch()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        var branch = await context.ConversationBranches.BranchAsync(Tenant, parent, upToSequence: -1);

        branch!.Value.CopiedItemCount.ShouldBe(0);
        branch.Value.BranchFromSequence.ShouldBe(-1);
        (await CountItemsAsync(context, branch.Value.ConversationId)).ShouldBe(0);
    }

    [Fact]
    public async Task Another_tenants_conversation_cannot_be_branched()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var parent = await SeedConversationAsync(context, itemCount: 2);

        (await context.ConversationBranches.BranchAsync("other-tenant", parent, upToSequence: null))
            .ShouldBeNull();
    }

    [Fact]
    public async Task A_nonexistent_conversation_returns_null()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        (await context.ConversationBranches.BranchAsync(Tenant, AgentPrismId.NewId(), upToSequence: null))
            .ShouldBeNull();
    }

    private static async ValueTask<Guid> SeedConversationAsync(
        SqlServerTestContext context,
        int itemCount,
        string agentName = "test-agent")
    {
        var conversationId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await context.ExecuteAsync(
            $"""
            INSERT INTO {context.SchemaName}.conversations (id, tenant_id, agent_name, metadata, created_at, updated_at)
            VALUES ('{conversationId}', '{Tenant}', '{agentName}', '{EmptyJsonObject}', '{now}', '{now}');
            """);

        for (var index = 0; index < itemCount; index++)
        {
            await InsertItemAsync(context, conversationId, index, $"message-{index}");
        }

        return conversationId;
    }

    private static async ValueTask InsertItemAsync(
        SqlServerTestContext context,
        Guid conversationId,
        long sequence,
        string text)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // Mimics a polymorphic ChatMessage: the `$type` discriminator is the
        // FIRST property and must survive the copy as-is (K-027).
        await context.ExecuteAsync(
            $$"""
            INSERT INTO {{context.SchemaName}}.conversation_items (id, conversation_id, seq, item, created_at)
            VALUES ('{{AgentPrismId.NewId()}}', '{{conversationId}}', {{sequence}},
                    '{"$type":"text","text":"{{text}}"}', '{{now}}');
            """);
    }

    private static async ValueTask<int> CountItemsAsync(SqlServerTestContext context, Guid conversationId)
        => await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.conversation_items WHERE conversation_id = '{conversationId}';");
}
