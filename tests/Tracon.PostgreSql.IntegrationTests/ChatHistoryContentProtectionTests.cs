using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Proves content protection through the REAL registration path
/// (<c>AddContentProtection(...).UsePostgreSql(...)</c>), not a hand-built
/// <see cref="SqlStoreContext"/> — closing the gap the other
/// <see cref="ContentProtectionTests"/> leave: those construct the context
/// directly and never exercise <c>TraconPostgreSqlBuilderExtensions</c>'s
/// own <c>IContentProtector</c>/<c>ProtectedColumns</c> resolution. It also
/// covers <c>conversation_items.item</c> (written by <c>SqlChatHistoryProvider</c>),
/// which needs a real running agent to produce — MAF's <c>ChatHistoryProvider</c>
/// hooks are protected and cannot be called directly (same rationale as
/// <see cref="DataSubjectChatHistoryTests"/>).
/// </summary>
public sealed class ChatHistoryContentProtectionTests(PostgresFixture fixture)
{
    private static readonly string TestKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray());

    [Fact]
    public async Task A_real_agent_run_encrypts_conversation_history_through_the_real_registration_path()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        const string SessionId = "cp-chat-history-subject";

        await using (var provider = BuildProvider(context))
        {
            var agent = await ResolveAsync(provider, "support");
            var manager = provider.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("secret-marker-in-conversation-history", session);
            await manager.SaveSessionAsync(agent, session);
        }

        var rawItem = await context.ScalarAsync<string>(
            $"SELECT item FROM {context.SchemaName}.conversation_items LIMIT 1;");
        rawItem.ShouldNotBeNull();
        rawItem.ShouldContain("$apEnc");
        rawItem.ShouldNotContain("secret-marker-in-conversation-history");

        var rawState = await context.ScalarAsync<string>(
            $"SELECT state FROM {context.SchemaName}.sessions WHERE id = '{SessionId}';");
        rawState.ShouldNotBeNull();
        rawState.ShouldContain("$apEnc");

        // The provider is resolved fresh (a new DI container, same schema) to
        // prove decryption does not depend on in-process state from the write:
        // a second turn in the SAME session only succeeds and grows the
        // conversation if the first (encrypted) turn was read back correctly -
        // ChatHistoryProvider's read hook is MAF-internal (protected) and
        // cannot be called directly, so a real second turn is the proof.
        await using var reader = BuildProvider(context);
        var readAgent = await ResolveAsync(reader, "support");
        var readManager = reader.GetRequiredService<AgentSessionManager>();
        var readSession = await readManager.GetOrCreateSessionAsync(readAgent, SessionId);
        await readAgent.RunAsync("second turn, same session", readSession);
        await readManager.SaveSessionAsync(readAgent, readSession);

        var itemCount = await context.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.conversation_items;");
        itemCount.ShouldBeGreaterThan(2, "the second turn's history did not build on the first (decrypt likely failed silently).");
    }

    private static async ValueTask<Microsoft.Agents.AI.AIAgent> ResolveAsync(IServiceProvider provider, string name)
        => await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(name, culture: null, CancellationToken.None)
           ?? throw new InvalidOperationException($"Could not resolve agent '{name}'.");

    private ServiceProvider BuildProvider(PostgresTestContext context)
    {
        var services = new ServiceCollection();

        services.AddSingleton(context.TenantContext);
        services.AddSingleton(_ => new FakeModelProvider("echo").EchoesUserMessage());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Keys:k1"] = TestKey })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddTracon()
            .AddModelProvider(static provider => provider.GetRequiredService<FakeModelProvider>())
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
            })
            .AddContentProtection(options =>
            {
                options.Enabled = true;
                options.ActiveKeyId = "k1";
                options.Keys["k1"] = "Keys:k1";
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
