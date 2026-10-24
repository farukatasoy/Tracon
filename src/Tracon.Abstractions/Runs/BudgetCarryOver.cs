namespace Tracon;

/// <summary>
/// What the earlier runs of one logical workflow run already spent, handed to the budget of
/// the run that continues them.
/// </summary>
/// <remarks>
/// A workflow that waits for a person, is resumed by an operator or is picked up after an
/// interruption is one run to its owner, but each step of that chain is a run row with its own
/// budget. The ceiling is the logical run's: a continuation starts from what the chain spent,
/// not from zero, so the cost, token, child-run and time limits mean what they say.
/// </remarks>
/// <param name="Tokens">The tokens the chain spent.</param>
/// <param name="Cost">The amount the chain spent, as <see cref="RunSpend"/> adds it.</param>
/// <param name="Runs">The child runs the chain started.</param>
/// <param name="ActiveDuration">
/// The time the chain's runs were executing. The time a run waited for a person is not in it:
/// the time limit bounds execution, not the person's answer.
/// </param>
internal readonly record struct BudgetCarryOver(long Tokens, decimal Cost, int Runs, TimeSpan ActiveDuration)
{
    /// <summary>Gets the carry-over of a run that continues nothing.</summary>
    public static BudgetCarryOver None => default;

    /// <summary>Gets whether the chain spent nothing and used no time.</summary>
    public bool IsEmpty => Tokens <= 0 && Cost <= 0m && Runs <= 0 && ActiveDuration <= TimeSpan.Zero;
}
