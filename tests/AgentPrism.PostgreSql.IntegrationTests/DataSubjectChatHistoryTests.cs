using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Closes the gap an independent phase 64 review found: an ordinary agent session's
/// chat history (written by <c>SqlChatHistoryProvider</c>) is reachable from
/// <see cref="DataSubjectScope.SessionIds"/> alone, without the consumer's own
/// <see cref="IDataSubjectResolver"/> needing to know AgentPrism's internal
/// conversation id.
/// </summary>
/// <remarks>
/// The internal conversation id lives inside the session's state bag (see
/// <see cref="SessionConversationResolver"/>'s remarks), so this needs a REAL
/// running agent to produce it — the same fixture <c>SessionPersistenceTests</c>
/// uses, not a raw-SQL seed.
/// </remarks>
public sealed class DataSubjectChatHistoryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SessionConversationResolver_finds_the_conversation_a_real_run_created()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        const string SessionId = "chat-history-subject";

        Guid conversationId;

        await using (var provider = BuildProvider(context))
        {
            var agent = await ResolveAsync(provider, "support");
            var manager = provider.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("where is my order", session);
            await manager.SaveSessionAsync(agent, session);

            conversationId = await context.ScalarAsync<Guid>(
                $"SELECT conversation_id FROM {context.SchemaName}.conversation_items ci " +
                $"JOIN {context.SchemaName}.conversations c ON c.id = ci.conversation_id LIMIT 1;");
        }

        await using (var provider = BuildProvider(context))
        {
            var resolver = new SessionConversationResolver(
                provider.GetRequiredService<ISessionStore>(),
                provider.GetRequiredService<IAgentCatalog>());

            var found = await resolver.ResolveAsync([SessionId]);

            found.ShouldHaveSingleItem().ShouldBe(conversationId);
        }
    }

    [Fact]
    public async Task Erasing_a_session_removes_its_real_chat_history()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        const string SessionId = "chat-history-erase-subject";

        await using (var provider = BuildProvider(context))
        {
            var agent = await ResolveAsync(provider, "support");
            var manager = provider.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("where is my order", session);
            await manager.SaveSessionAsync(agent, session);
        }

        var itemsBefore = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.conversation_items;");
        itemsBefore.ShouldBeGreaterThan(0, "the run should have produced chat history to erase");

        await using (var provider = BuildProvider(context))
        {
            var resolver = new SessionConversationResolver(
                provider.GetRequiredService<ISessionStore>(),
                provider.GetRequiredService<IAgentCatalog>());

            var conversationIds = await resolver.ResolveAsync([SessionId]);

            await context.DataSubjects.EraseAsync(
                context.TenantContext.TenantId,
                new DataSubjectScope { SessionIds = [SessionId], RunIds = [], ConversationIds = conversationIds },
                static (_, _) => ValueTask.CompletedTask);
        }

        var itemsAfter = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.conversation_items;");

        itemsAfter.ShouldBe(0);
    }

    [Fact]
    public async Task A_session_whose_agent_no_longer_exists_is_skipped_not_thrown()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        const string SessionId = "orphaned-agent-session";

        await using (var provider = BuildProvider(context))
        {
            var agent = await ResolveAsync(provider, "support");
            var manager = provider.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("hello", session);
            await manager.SaveSessionAsync(agent, session);
        }

        // A second provider that never registers the "support" agent —
        // simulates an agent definition removed after the session was created.
        var services = new ServiceCollection();
        services.AddSingleton(context.TenantContext);
        services.AddAgentPrism().UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.SchemaName = context.SchemaName;
            options.AutoApplyMigrations = false;
        });

        await using var withoutAgent = services.BuildServiceProvider();

        var resolver = new SessionConversationResolver(
            withoutAgent.GetRequiredService<ISessionStore>(),
            withoutAgent.GetRequiredService<IAgentCatalog>());

        var found = await resolver.ResolveAsync([SessionId]);

        found.ShouldBeEmpty();
    }

    private static async ValueTask<Microsoft.Agents.AI.AIAgent> ResolveAsync(IServiceProvider provider, string name)
        => await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(name, culture: null, CancellationToken.None)
           ?? throw new InvalidOperationException($"Could not resolve agent '{name}'.");

    private ServiceProvider BuildProvider(PostgresTestContext context)
    {
        var services = new ServiceCollection();

        services.AddSingleton(context.TenantContext);
        services.AddSingleton(_ => new FakeModelProvider("echo").EchoesUserMessage());

        services.AddAgentPrism()
            .AddModelProvider(static provider => provider.GetRequiredService<FakeModelProvider>())
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
            })
            .UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.SchemaName = context.SchemaName;
                options.AutoApplyMigrations = false;
            });

        return services.BuildServiceProvider();
    }
}
