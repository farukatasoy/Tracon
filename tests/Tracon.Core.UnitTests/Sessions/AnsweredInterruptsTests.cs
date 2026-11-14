using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Sessions;

/// <summary>
/// What a session holds of the answers a request applied and no run has saved yet.
/// </summary>
public sealed class AnsweredInterruptsTests
{
    private static readonly IReadOnlySet<string> NoHistory = new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public async Task An_answer_is_held_with_the_session_and_read_back_after_the_session_is_serialized()
    {
        var agent = new ChatClientAgent(new FakeChatClient());
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        AnsweredInterrupts.Read(session).ShouldBeEmpty();
        AnsweredInterrupts.Write(session, ["approval-1", "approval-2"], NoHistory).ShouldBeTrue();

        var state = await agent.SerializeSessionAsync(session, jsonSerializerOptions: null, TestContext.Current.CancellationToken);
        var restored = await agent.DeserializeSessionAsync(state, jsonSerializerOptions: null, TestContext.Current.CancellationToken);

        AnsweredInterrupts.Read(restored).OrderBy(static id => id, StringComparer.Ordinal).ShouldBe(["approval-1", "approval-2"]);
    }

    [Fact]
    public async Task An_answer_the_stored_history_holds_leaves_the_set_and_an_empty_set_leaves_no_key()
    {
        var session = await new ChatClientAgent(new FakeChatClient()).CreateSessionAsync(TestContext.Current.CancellationToken);

        AnsweredInterrupts.Write(session, ["approval-1", "approval-2"], NoHistory);

        AnsweredInterrupts.Write(session, [], new HashSet<string>(["approval-1"], StringComparer.Ordinal)).ShouldBeTrue();
        AnsweredInterrupts.Read(session).ShouldBe(["approval-2"]);

        AnsweredInterrupts.Write(session, [], new HashSet<string>(["approval-2"], StringComparer.Ordinal)).ShouldBeTrue();
        AnsweredInterrupts.Read(session).ShouldBeEmpty();
        session.StateBag.TryGetValue<string>(AnsweredInterrupts.Key, out _, Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions).ShouldBeFalse();
    }

    [Fact]
    public async Task A_write_that_changes_nothing_says_so_and_a_repeated_answer_is_held_once()
    {
        var session = await new ChatClientAgent(new FakeChatClient()).CreateSessionAsync(TestContext.Current.CancellationToken);

        AnsweredInterrupts.Write(session, ["approval-1", "approval-1"], NoHistory).ShouldBeTrue();
        AnsweredInterrupts.Write(session, ["approval-1"], NoHistory).ShouldBeFalse("the answer was held already");
        AnsweredInterrupts.Read(session).ShouldBe(["approval-1"]);
    }

    [Fact]
    public async Task An_identity_that_holds_a_line_break_or_nothing_is_not_held()
    {
        var session = await new ChatClientAgent(new FakeChatClient()).CreateSessionAsync(TestContext.Current.CancellationToken);

        AnsweredInterrupts.Write(session, ["a\nb", string.Empty], NoHistory).ShouldBeFalse();
        AnsweredInterrupts.Read(session).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_set_is_bounded_and_drops_the_oldest_answers_first()
    {
        var session = await new ChatClientAgent(new FakeChatClient()).CreateSessionAsync(TestContext.Current.CancellationToken);

        AnsweredInterrupts.Write(session, Enumerable.Range(0, AnsweredInterrupts.MaxIdentities + 10).Select(static n => $"approval-{n}"), NoHistory);

        var held = AnsweredInterrupts.Read(session);

        held.Count.ShouldBe(AnsweredInterrupts.MaxIdentities);
        held.Contains("approval-0").ShouldBeFalse();
        held.Contains($"approval-{AnsweredInterrupts.MaxIdentities + 9}").ShouldBeTrue();
    }
}
