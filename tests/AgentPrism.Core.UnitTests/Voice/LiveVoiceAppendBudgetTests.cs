namespace AgentPrism.Core.UnitTests.Voice;

/// <summary>
/// Verifies the two ceilings a delegation's output passes through before it reaches
/// the provider.
/// </summary>
/// <remarks>
/// The character ceiling is the provider's own per-append limit; text over it is
/// <strong>split</strong>, never dropped, because half an answer is worse than a
/// slow one. The append count bounds how much one delegation may push into a session
/// the provider is driving.
/// </remarks>
public sealed class LiveVoiceAppendBudgetTests
{
    [Fact]
    public void Text_inside_the_ceiling_is_one_piece()
    {
        var budget = new LiveVoiceAppendBudget(maxCharacters: 100, maxAppends: 10);

        budget.Take("Order 442 shipped on Tuesday.")
            .ShouldHaveSingleItem()
            .ShouldBe("Order 442 shipped on Tuesday.");
    }

    [Fact]
    public void Oversized_text_is_SPLIT_not_dropped()
    {
        var budget = new LiveVoiceAppendBudget(maxCharacters: 20, maxAppends: 10);
        var text = string.Join(' ', Enumerable.Repeat("word", 20));

        var pieces = budget.Take(text);

        pieces.Count.ShouldBeGreaterThan(1);
        pieces.ShouldAllBe(piece => piece.Length <= 20);

        // Nothing is lost: every word survives the split.
        string.Join(' ', pieces).Replace("  ", " ", StringComparison.Ordinal).ShouldBe(text);
    }

    [Fact]
    public void A_split_prefers_a_word_boundary()
    {
        var budget = new LiveVoiceAppendBudget(maxCharacters: 12, maxAppends: 10);

        var pieces = budget.Take("alpha beta gamma delta");

        pieces[0].ShouldBe("alpha beta");
        pieces[0].ShouldNotEndWith(" ");
    }

    [Fact]
    public void A_single_word_longer_than_the_ceiling_is_cut_at_the_ceiling()
    {
        // The alternative is sending something the provider refuses outright.
        var budget = new LiveVoiceAppendBudget(maxCharacters: 20, maxAppends: 10);

        var pieces = budget.Take(new string('z', 55));

        pieces.Count.ShouldBe(3);
        pieces[0].Length.ShouldBe(20);
    }

    [Fact]
    public void The_append_ceiling_stops_further_pieces()
    {
        var budget = new LiveVoiceAppendBudget(maxCharacters: 10, maxAppends: 2);

        var pieces = budget.Take(string.Join(' ', Enumerable.Repeat("abcde", 20)));

        pieces.Count.ShouldBe(2);
        budget.IsExhausted.ShouldBeTrue();
        budget.Take("anything more").ShouldBeEmpty();
    }

    [Fact]
    public void The_budget_is_charged_across_calls()
    {
        var budget = new LiveVoiceAppendBudget(maxCharacters: 100, maxAppends: 3);

        budget.Take("one").Count.ShouldBe(1);
        budget.Take("two").Count.ShouldBe(1);
        budget.Sent.ShouldBe(2);
        budget.IsExhausted.ShouldBeFalse();

        budget.Take("three").Count.ShouldBe(1);
        budget.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void Blank_text_costs_nothing()
    {
        var budget = new LiveVoiceAppendBudget(maxCharacters: 100, maxAppends: 3);

        budget.Take(null).ShouldBeEmpty();
        budget.Take(string.Empty).ShouldBeEmpty();
        budget.Take("   ").ShouldBeEmpty();
        budget.Sent.ShouldBe(0);
    }
}
