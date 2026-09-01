using System.Globalization;
using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Sessions;

/// <summary>
/// Phase 126: the compatibility promise for persisted session state.
/// </summary>
/// <remarks>
/// <see cref="Todays_code_reads_a_session_state_fixture_written_by_todays_MAF"/>
/// is the upgrade gate: it reads a session state that was genuinely captured
/// from a real run (see <c>Fixtures/README.md</c>), not hand-written. Its
/// value is not in staying green — it is in what a RED result says. When
/// <c>Directory.Packages.props</c>'s <c>MicrosoftAgentsAIVersion</c> moves,
/// this is the first place that notices a session written by the OLD
/// version cannot be read by the NEW one. A break is renewed by adding a
/// new fixture, never by replacing this one — see the README.
/// </remarks>
public sealed class PersistedPayloadUpgradeTests
{
    [Fact]
    public async Task Todays_code_reads_a_session_state_fixture_written_by_todays_MAF()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "session-state-1.18.0.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));

        var session = await Agent().DeserializeSessionAsync(
            document.RootElement, jsonSerializerOptions: null, CancellationToken.None);

        session.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_future_schema_generation_is_a_defined_error_not_a_guess_and_the_session_survives()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        await store.SaveAsync(new SessionRecord
        {
            Id = "future",
            AgentName = agent.Name!,
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            StateSchemaVersion = int.MaxValue,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await manager.GetOrCreateSessionAsync(agent, "future"));

        // Measured facts, not a guess: both the recorded generation and the
        // one this build understands.
        exception.Message.ShouldContain(int.MaxValue.ToString(CultureInfo.InvariantCulture));
        exception.Message.ShouldContain(AgentSessionManager.CurrentStateSchemaVersion.ToString(CultureInfo.InvariantCulture));

        (await store.GetAsync("future")).ShouldNotBeNull("an unreadable session is never silently deleted or reset");
    }

    [Fact]
    public async Task An_unreadable_state_reports_the_recorded_and_current_MAF_version_instead_of_guessing()
    {
        var store = new InMemorySessionStore(FixedTenantContext.Default);
        var manager = new AgentSessionManager(store, FixedTenantContext.Default);
        var agent = Agent();

        // A shape MAF's own deserializer cannot make sense of -- the same
        // class of failure a real cross-version incompatibility produces.
        await store.SaveAsync(new SessionRecord
        {
            Id = "corrupt",
            AgentName = agent.Name!,
            State = JsonDocument.Parse("""{"stateBag":"this should be an object, not a string"}""").RootElement.Clone(),
            StateSchemaVersion = AgentSessionManager.CurrentStateSchemaVersion,
            StateMafVersion = "1.0.0-older",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await manager.GetOrCreateSessionAsync(agent, "corrupt"));

        // The recorded version must be named, not guessed at, and the old
        // guessing text must be gone.
        exception.Message.ShouldContain("1.0.0-older");
        exception.Message.ShouldNotContain("may have become unreadable");

        (await store.GetAsync("corrupt")).ShouldNotBeNull("an unreadable session is never silently deleted or reset");
    }

    private static AIAgent Agent()
        => new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider(new FakeChatClient())),
                TestData.Registry())
            .Compile(TestData.Definition());
}
