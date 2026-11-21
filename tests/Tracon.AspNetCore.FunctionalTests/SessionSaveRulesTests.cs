using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a host that runs an agent in process can rely on when it opens, saves and reads a session. The rules are not
/// obvious from the calls, so each is pinned here against a real host on both storage modes.
/// </summary>
/// <remarks>
/// <para>
/// A turn's messages are written to the history when the turn ends, and the session record that names the
/// conversation is written when the host saves. The two can disagree: a turn that was never saved, a turn that lost a
/// race at the save, a turn that was canceled. The cases name which messages survive each.
/// </para>
/// </remarks>
public sealed class SessionSaveRulesTests
{
    private const string AgentName = "kod-agent";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_session_that_is_never_saved_is_not_there_for_the_next_turn(bool sqlStorage)
    {
        using var database = new TempSqliteDatabase("save-rules-new");

        await using var host = await StartAsync(database, sqlStorage);
        var (manager, agent) = await ResolveAsync(host);

        var first = await manager.GetOrCreateSessionAsync(agent, "s-new", TestContext.Current.CancellationToken);

        await agent.RunAsync("first turn", first, cancellationToken: TestContext.Current.CancellationToken);

        (await manager.ReadHistoryAsync(agent, "s-new", cancellationToken: TestContext.Current.CancellationToken))
            .ShouldBeNull("nothing named this session: the host never saved it");

        var next = await manager.GetOrCreateSessionAsync(agent, "s-new", TestContext.Current.CancellationToken);

        await agent.RunAsync("second turn", next, cancellationToken: TestContext.Current.CancellationToken);
        await manager.SaveSessionAsync(agent, next, TestContext.Current.CancellationToken);

        var texts = TextsOf(await manager.ReadHistoryAsync(agent, "s-new", cancellationToken: TestContext.Current.CancellationToken));

        texts.ShouldContain("second turn", StringComparer.Ordinal);
        texts.ShouldNotContain("first turn", StringComparer.Ordinal, "the first turn answered in a session nobody could find again");
    }

    [Fact]
    public async Task On_SQL_storage_a_later_turn_that_is_not_saved_is_still_in_the_conversation()
    {
        using var database = new TempSqliteDatabase("save-rules-sql");

        await using var host = await StartAsync(database, sqlStorage: true);
        var (manager, agent) = await ResolveAsync(host);

        await TurnAsync(manager, agent, "s-sql", "saved turn", save: true);
        await TurnAsync(manager, agent, "s-sql", "unsaved turn", save: false);

        TextsOf(await manager.ReadHistoryAsync(agent, "s-sql", cancellationToken: TestContext.Current.CancellationToken))
            .ShouldContain("unsaved turn", StringComparer.Ordinal, "the conversation is stored under an id the first save already wrote into the record");
    }

    [Fact]
    public async Task With_the_in_memory_default_a_later_turn_that_is_not_saved_is_lost()
    {
        await using var host = await StartAsync(database: null, sqlStorage: false);
        var (manager, agent) = await ResolveAsync(host);

        await TurnAsync(manager, agent, "s-memory", "saved turn", save: true);
        await TurnAsync(manager, agent, "s-memory", "unsaved turn", save: false);

        var texts = TextsOf(await manager.ReadHistoryAsync(agent, "s-memory", cancellationToken: TestContext.Current.CancellationToken));

        texts.ShouldContain("saved turn", StringComparer.Ordinal);
        texts.ShouldNotContain("unsaved turn", StringComparer.Ordinal, "the history lives in the session's state, and the state was not saved");
    }

    [Fact]
    public async Task A_turn_that_loses_the_save_race_still_has_its_messages_in_the_conversation()
    {
        using var database = new TempSqliteDatabase("save-rules-race");

        await using var host = await StartAsync(database, sqlStorage: true);
        var (manager, agent) = await ResolveAsync(host);

        await TurnAsync(manager, agent, "s-race", "opening turn", save: true);

        // Two callers restore the same record, answer, and then save: the second save finds the record changed.
        var winner = await manager.GetOrCreateSessionAsync(agent, "s-race", TestContext.Current.CancellationToken);
        var loser = await manager.GetOrCreateSessionAsync(agent, "s-race", TestContext.Current.CancellationToken);

        await agent.RunAsync("from the winner", winner, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("from the loser", loser, cancellationToken: TestContext.Current.CancellationToken);

        await manager.SaveSessionAsync(agent, winner, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<TraconSessionConflictException>(
            async () => await manager.SaveSessionAsync(agent, loser, TestContext.Current.CancellationToken));

        var texts = TextsOf(await manager.ReadHistoryAsync(agent, "s-race", cancellationToken: TestContext.Current.CancellationToken));

        texts.ShouldContain("from the winner", StringComparer.Ordinal);
        texts.ShouldContain("from the loser", StringComparer.Ordinal, "the history is written as the turn ends, before the save that was refused");
    }

    private static async Task TurnAsync(AgentSessionManager manager, AIAgent agent, string sessionId, string message, bool save)
    {
        var session = await manager.GetOrCreateSessionAsync(agent, sessionId, TestContext.Current.CancellationToken);

        await agent.RunAsync(message, session, cancellationToken: TestContext.Current.CancellationToken);

        if (save)
        {
            await manager.SaveSessionAsync(agent, session, TestContext.Current.CancellationToken);
        }
    }

    private static string[] TextsOf(IReadOnlyList<ChatMessage>? messages)
        => [.. (messages ?? []).Select(static message => message.Text)];

    private static async Task<(AgentSessionManager Manager, AIAgent Agent)> ResolveAsync(TraconTestHost host)
    {
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(AgentName, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        return (host.Services.GetRequiredService<AgentSessionManager>(), agent);
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase? database, bool sqlStorage)
        => TraconTestHost.StartAsync(builder =>
        {
            if (sqlStorage)
            {
                builder.UseSqlite(database!.ConnectionString);
            }

            builder.AddAgent(TestData.Definition());
        });
}
