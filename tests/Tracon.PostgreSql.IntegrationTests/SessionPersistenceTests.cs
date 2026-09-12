using Microsoft.Extensions.DependencyInjection;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Verifies that session persistence works end to end.
/// </summary>
/// <remarks>
/// Two open items carried over from Phase 1 are closed by these tests:
/// <see cref="ISessionStore"/> is now used, and <c>RunRecordingAgent</c>
/// writes the real session ID into the run record.
/// </remarks>
public sealed class SessionPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Session_is_saved_and_restored_in_a_new_process()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "customer-42";

        // First "process": open the session, run it, save it.
        await using (var first = BuildProvider(context))
        {
            var agent = await ResolveAsync(first, "support");
            var manager = first.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("where is my order", session);
            await manager.SaveSessionAsync(agent, session);
        }

        // Second "process": resume with the same ID.
        await using (var second = BuildProvider(context))
        {
            var agent = await ResolveAsync(second, "support");
            var manager = second.GetRequiredService<AgentSessionManager>();

            var restored = await manager.GetOrCreateSessionAsync(agent, SessionId);

            AgentSessionIdentity.GetId(restored).ShouldBe(SessionId);

            var stored = await second.GetRequiredService<ISessionStore>().GetAsync(SessionId);
            stored.ShouldNotBeNull();
            stored.AgentName.ShouldBe("support");
        }
    }

    [Fact]
    public async Task Chat_history_survives_across_sessions()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "history-session";

        await using (var first = BuildProvider(context))
        {
            var agent = await ResolveAsync(first, "support");
            var manager = first.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("first question", session);
            await manager.SaveSessionAsync(agent, session);
        }

        await using (var second = BuildProvider(context))
        {
            var agent = await ResolveAsync(second, "support");
            var manager = second.GetRequiredService<AgentSessionManager>();
            var provider = second.GetRequiredService<FakeModelProvider>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("second question", session);

            // The model must also see the first turn's messages; they came from the database history.
            var texts = provider.Requests[^1].Messages.Select(static message => message.Text).ToList();

            texts.ShouldContain(static text => text.Contains("first question", StringComparison.Ordinal));
            texts.ShouldContain(static text => text.Contains("second question", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Two_concurrent_first_turns_on_the_same_new_session_do_not_silently_lose_a_message()
    {
        // HATA-004 / MT-CORE-054: in the check-then-create race, two
        // concurrent first turns generated different conversation IDs for
        // the same NEW session; the second SaveSessionAsync SILENTLY
        // overwrote the first, and the loser's messages became unreachable
        // — both calls appeared to "succeed".
        //
        // Accepted behavior (KAPANIS-PLANI.md §6, Family C): either NO
        // message is lost, or, if a conflict check exists, one of the
        // requests returns an EXPLICIT conflict error. This second branch is
        // acceptable — silent loss is NOT. After the fix: of two concurrent
        // first turns, EXACTLY ONE succeeds, the other gets
        // TraconSessionConflictException; the two never both silently
        // "succeed" with one lost.
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "concurrent-first-turn";

        await using var first = BuildProvider(context);
        await using var second = BuildProvider(context);

        static async Task<Exception?> TryRunFirstTurnAsync(ServiceProvider provider, string message)
        {
            try
            {
                var agent = await ResolveAsync(provider, "support");
                var manager = provider.GetRequiredService<AgentSessionManager>();

                var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
                await agent.RunAsync(message, session);
                await manager.SaveSessionAsync(agent, session);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var outcomes = await Task.WhenAll(
            TryRunFirstTurnAsync(first, "first request"),
            TryRunFirstTurnAsync(second, "second request"));

        // In the silent-loss scenario both would return null (success) but
        // only 2 messages would remain in the session. Accepted behavior: at
        // most one fails, and the one that fails MUST be an explicit conflict error.
        outcomes.Count(static ex => ex is null).ShouldBeGreaterThanOrEqualTo(1, "Both requests failed.");
        outcomes.Where(static ex => ex is not null).ShouldAllBe(static ex => ex is TraconSessionConflictException);
    }

    [Fact]
    public async Task Two_concurrent_later_turns_on_the_same_existing_session_do_not_silently_lose_a_message()
    {
        // 🚨 The sibling of the HATA-004 case above, and the half that stayed
        // open: TryCreateAsync closed the race for the FIRST save only. Every
        // save after it went through the unconditional SaveAsync, so two
        // concurrent LATER turns on an EXISTING session both "succeeded" and
        // the loser's turn was silently overwritten.
        //
        // Accepted behavior is the same as the first-turn case: at most one
        // fails, and the one that fails must be an EXPLICIT conflict — never
        // two silent successes with one turn missing.
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "concurrent-later-turn";

        // Establish the session first, so both racing turns are SUBSEQUENT
        // saves rather than first ones.
        await using (var seed = BuildProvider(context))
        {
            var agent = await ResolveAsync(seed, "support");
            var manager = seed.GetRequiredService<AgentSessionManager>();

            var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
            await agent.RunAsync("opening turn", session);
            await manager.SaveSessionAsync(agent, session);
        }

        await using var first = BuildProvider(context);
        await using var second = BuildProvider(context);

        static async Task<Exception?> TryRunLaterTurnAsync(ServiceProvider provider, string message)
        {
            try
            {
                var agent = await ResolveAsync(provider, "support");
                var manager = provider.GetRequiredService<AgentSessionManager>();

                var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
                await agent.RunAsync(message, session);
                await manager.SaveSessionAsync(agent, session);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var outcomes = await Task.WhenAll(
            TryRunLaterTurnAsync(first, "first later request"),
            TryRunLaterTurnAsync(second, "second later request"));

        outcomes.Count(static ex => ex is null).ShouldBeGreaterThanOrEqualTo(1, "Both requests failed.");
        outcomes.Where(static ex => ex is not null).ShouldAllBe(static ex => ex is TraconSessionConflictException);

        // The decisive assertion: exactly one of the two turns may be the
        // stored outcome, and the OTHER one must have been told. Two silent
        // successes are what this test exists to forbid.
        outcomes.Count(static ex => ex is null).ShouldBe(1);
    }

    [Fact]
    public async Task Run_record_carries_the_real_session_id()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        const string SessionId = "recorded-session";

        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "support");
        var manager = provider.GetRequiredService<AgentSessionManager>();

        var session = await manager.GetOrCreateSessionAsync(agent, SessionId);
        await agent.RunAsync("hello", session);

        var runs = await provider.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery());

        var run = runs.ShouldHaveSingleItem();
        run.SessionId.ShouldBe(SessionId);
        run.AgentName.ShouldBe("support");
        run.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Run_without_a_session_carries_a_null_session_id()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "support");
        await agent.RunAsync("no session");

        var runs = await provider.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery());

        runs.ShouldHaveSingleItem().SessionId.ShouldBeNull();
    }

    [Fact]
    public async Task Session_without_an_id_cannot_be_saved()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "support");
        var manager = provider.GetRequiredService<AgentSessionManager>();

        // A session opened outside AgentSessionManager has no Tracon ID.
        var session = await agent.CreateSessionAsync();

        await Should.ThrowAsync<TraconException>(async () => await manager.SaveSessionAsync(agent, session));
    }

    [Fact]
    public async Task Restoring_a_deleted_session_opens_a_new_one()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        await using var provider = BuildProvider(context);

        var agent = await ResolveAsync(provider, "support");
        var manager = provider.GetRequiredService<AgentSessionManager>();

        var session = await manager.GetOrCreateSessionAsync(agent, "temporary");
        await manager.SaveSessionAsync(agent, session);

        (await manager.DeleteSessionAsync("temporary")).ShouldBeTrue();
        (await manager.DeleteSessionAsync("temporary")).ShouldBeFalse();

        var fresh = await manager.GetOrCreateSessionAsync(agent, "temporary");
        AgentSessionIdentity.GetId(fresh).ShouldBe("temporary");
    }

    private static async ValueTask<Microsoft.Agents.AI.AIAgent> ResolveAsync(IServiceProvider provider, string name)
        => await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(name, culture: null, CancellationToken.None)
           ?? throw new InvalidOperationException($"Could not resolve agent '{name}'.");

    private ServiceProvider BuildProvider(PostgresTestContext context)
    {
        var services = new ServiceCollection();

        services.AddSingleton(context.TenantContext);
        services.AddSingleton(_ => new FakeModelProvider("echo").EchoesUserMessage());

        services.AddTracon()
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
