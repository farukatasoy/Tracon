namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// Verifies the ledger a live delegation's prompt is cut from.
/// </summary>
/// <remarks>
/// The provider's delegation event carries no task text, only a point in time. If
/// the cut is wrong the delegated run answers the wrong question, and nothing else in
/// the chain can notice.
/// </remarks>
public sealed class LiveTranscriptLedgerTests
{
    [Fact]
    public void Consecutive_deltas_from_one_speaker_become_one_entry()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, " Order", 600, 800);
        ledger.Append(LiveTranscriptRole.User, " status", 1000, 1200);
        ledger.Append(LiveTranscriptRole.User, " for 442", 1400, 1600);
        ledger.Commit();

        var entry = ledger.Snapshot().ShouldHaveSingleItem();

        entry.Role.ShouldBe(LiveTranscriptRole.User);
        entry.Text.ShouldBe("Order status for 442");
        entry.StartMilliseconds.ShouldBe(600);
        entry.EndMilliseconds.ShouldBe(1600);
    }

    [Fact]
    public void A_change_of_speaker_closes_the_entry()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, "Where is my order?", 0, 500);
        ledger.Append(LiveTranscriptRole.Assistant, "Checking that now.", 600, 900);
        ledger.Commit();

        ledger.Snapshot().Count.ShouldBe(2);
        ledger.Snapshot()[0].Role.ShouldBe(LiveTranscriptRole.User);
        ledger.Snapshot()[1].Role.ShouldBe(LiveTranscriptRole.Assistant);
    }

    [Fact]
    public void The_cut_includes_the_utterance_the_delegation_was_made_on()
    {
        // 🚨 Measured against the real provider: offset_ms equals the START of the
        // last transcript segment before the delegation. A cut that used a strict
        // "before the offset" test would drop the very sentence the delegation is
        // about.
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, "Look up order 442", 3800, 4200);
        ledger.Commit();

        var cut = ledger.Cut(offsetMilliseconds: 3800, maxEntries: 10);

        cut.ShouldHaveSingleItem().Text.ShouldBe("Look up order 442");
    }

    [Fact]
    public void The_cut_commits_the_entry_still_being_built()
    {
        // The provider sends the final word's delta and the delegation event in the
        // same instant; without the commit inside Cut the ledger would routinely
        // hand back a prompt missing its last sentence.
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, "Look up order 442", 3800, 4200);

        var cut = ledger.Cut(offsetMilliseconds: 3800, maxEntries: 10);

        cut.ShouldHaveSingleItem().Text.ShouldBe("Look up order 442");
    }

    [Fact]
    public void Entries_after_the_offset_are_not_in_the_cut()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, "First question", 0, 500);
        ledger.Append(LiveTranscriptRole.Assistant, "First answer", 600, 900);
        ledger.Append(LiveTranscriptRole.User, "Second question", 5000, 5400);
        ledger.Commit();

        var cut = ledger.Cut(offsetMilliseconds: 900, maxEntries: 10);

        cut.Count.ShouldBe(2);
        cut[^1].Text.ShouldBe("First answer");
    }

    [Fact]
    public void The_cut_keeps_only_the_newest_entries_it_is_allowed()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        for (var index = 0; index < 10; index++)
        {
            ledger.Append(LiveTranscriptRole.User, $"line {index}", index * 100, (index * 100) + 50);
            ledger.Append(LiveTranscriptRole.Assistant, $"reply {index}", (index * 100) + 60, (index * 100) + 90);
        }

        ledger.Commit();

        var cut = ledger.Cut(offsetMilliseconds: 100_000, maxEntries: 3);

        cut.Count.ShouldBe(3);
        cut[^1].Text.ShouldBe("reply 9");
    }

    [Fact]
    public void An_empty_conversation_produces_an_empty_cut()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Cut(offsetMilliseconds: 5000, maxEntries: 10).ShouldBeEmpty();
    }

    [Fact]
    public void The_ledger_drops_its_oldest_entries_rather_than_growing_without_limit()
    {
        // This is where the "oversized input" failure mode is closed: a
        // conversation that runs for an hour must not accumulate an hour of text.
        var ledger = new LiveTranscriptLedger(200);

        for (var index = 0; index < 50; index++)
        {
            ledger.Append(LiveTranscriptRole.User, new string('x', 40), index * 100, (index * 100) + 50);
            ledger.Commit();
        }

        var kept = ledger.Snapshot().Sum(entry => entry.Text.Length);

        kept.ShouldBeLessThanOrEqualTo(240);
        ledger.Snapshot().ShouldNotBeEmpty();
    }

    [Fact]
    public void Blank_deltas_are_ignored()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, null, 0, 10);
        ledger.Append(LiveTranscriptRole.User, string.Empty, 0, 10);
        ledger.Append(LiveTranscriptRole.User, "   ", 0, 10);
        ledger.Commit();

        ledger.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public void The_rendered_prompt_labels_each_speaker()
    {
        var ledger = new LiveTranscriptLedger(10_000);

        ledger.Append(LiveTranscriptRole.User, "Where is order 442?", 0, 500);
        ledger.Append(LiveTranscriptRole.Assistant, "Checking now.", 600, 900);

        var prompt = LiveTranscriptLedger.RenderPrompt(ledger.Cut(1000, 10));

        prompt.ShouldContain("User: Where is order 442?");
        prompt.ShouldContain("Assistant: Checking now.");
    }
}
