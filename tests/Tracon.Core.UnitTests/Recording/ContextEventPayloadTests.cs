using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The <see cref="RunEventCustomTypes.Context"/> draft built for one model request. It
/// holds counts by source in a fixed order and never a message.
/// </summary>
public sealed class ContextEventPayloadTests
{
    private static ChatMessage From(string type, string? id, string text = "secret text")
        => new ChatMessage(ChatRole.User, text).WithAgentRequestMessageSource(new AgentRequestMessageSourceType(type), id);

    [Fact]
    public void A_message_with_no_source_counts_as_the_callers_own_with_no_id()
    {
        // The framework reports an unattributed message as External; that is what the plan left to measure.
        var draft = ContextEventPayload.Describe([new ChatMessage(ChatRole.User, "hello")]);

        Sources(draft).ShouldBe([("External", null, 1)]);
    }

    [Fact]
    public void Messages_are_counted_by_type_and_id_in_a_fixed_order()
    {
        var draft = ContextEventPayload.Describe(
        [
            From("AIContextProvider", "memory"),
            From("External", null),
            From("ChatHistory", "history"),
            From("AIContextProvider", "files"),
            From("ChatHistory", "history"),
            From("AIContextProvider", "memory"),
            From("ChatHistory", "history"),
        ]);

        Sources(draft).ShouldBe(
        [
            ("AIContextProvider", "files", 1),
            ("AIContextProvider", "memory", 2),
            ("ChatHistory", "history", 3),
            ("External", null, 1),
        ]);
    }

    [Fact]
    public void The_same_request_gives_the_same_payload_whatever_the_message_order()
    {
        ChatMessage[] first = [From("ChatHistory", "h"), From("AIContextProvider", "m"), From("External", null)];
        ChatMessage[] second = [first[2], first[1], first[0]];

        ContextEventPayload.Describe(first).Payload.ShouldBe(ContextEventPayload.Describe(second).Payload);
    }

    [Fact]
    public void A_source_type_the_framework_adds_later_is_counted_under_its_own_name()
    {
        Sources(ContextEventPayload.Describe([From("Retrieval", "kb")])).ShouldBe([("Retrieval", "kb", 1)]);
    }

    [Fact]
    public void The_text_gives_the_total_and_the_payload_carries_no_message()
    {
        var draft = ContextEventPayload.Describe([From("External", null, "a private question"), From("AIContextProvider", "m", "a private memory")]);

        draft.Type.ShouldBe(RunEventType.Custom);
        draft.CustomType.ShouldBe(RunEventCustomTypes.Context);
        draft.Text.ShouldBe("context: 2 messages");
        draft.Payload!.ShouldNotContain("private");
    }

    [Fact]
    public void An_empty_request_is_an_empty_list_of_sources()
    {
        var draft = ContextEventPayload.Describe([]);

        draft.Text.ShouldBe("context: 0 messages");
        Sources(draft).ShouldBeEmpty();
    }

    private static List<(string Type, string? Id, int Count)> Sources(RunEventDraft draft)
    {
        using var payload = JsonDocument.Parse(draft.Payload!);

        payload.RootElement.GetProperty("type").GetString().ShouldBe("context");

        return [.. payload.RootElement.GetProperty("sources").EnumerateArray().Select(static source => (
            source.GetProperty("type").GetString()!,
            source.TryGetProperty("id", out var id) ? id.GetString() : null,
            source.GetProperty("count").GetInt32()))];
    }
}
