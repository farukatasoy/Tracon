namespace Tracon;

/// <summary>
/// What a run spent that is not tokens: the amounts its workflow function nodes and
/// its tools reported, added up in one currency.
/// </summary>
/// <remarks>
/// <para>
/// A run's token cost (<see cref="RunCost"/>) is priced by Tracon from the model's
/// usage. A text-to-speech character, a render second or a GPU hour is not a
/// token: the code that did the work reports its own amount
/// (<see cref="ToolCallUsage.Cost"/>). The reported amounts of one currency add up
/// here, in a total of their own. <strong>It is not a term of
/// <see cref="RunCost.Total"/></strong> (a token total and a reported amount are
/// different measurements, and <see cref="RunCost"/> stays a price snapshot of one
/// model's tokens). A caller that needs "what did this run spend" adds the two with
/// <see cref="RunSpend"/> and nothing else.
/// </para>
/// <para>
/// The value is a snapshot written once when the run closes. Items reported after
/// that are kept in the run's detail
/// (<see cref="IRunStore.ListNonTokenUsageAsync"/>) and do not change it. A run
/// that reported nothing holds <see langword="null"/>, not zero.
/// </para>
/// </remarks>
public sealed record RunNonTokenCost
{
    /// <summary>
    /// Gets the sum of the reported amounts that were added, or <see langword="null"/>
    /// when no reported item carried an amount in <c>Currency</c>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> says "nothing here could be priced", not "it was free":
    /// zero would claim a measurement nobody made. The same rule as the token cost's total.
    /// </remarks>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Gets the currency of <c>Amount</c>, taken from
    /// <c>Tracon:Pricing:Currency</c> (an item that names no currency is in it).
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Gets whether any added amount rests on an estimated quantity
    /// (<c>ToolCallUsage.IsEstimated</c>).
    /// </summary>
    /// <remarks>
    /// An estimated amount is added, and this flag says so: leaving it out would let
    /// a tenant's quota run past its ceiling on a quantity nobody measured.
    /// </remarks>
    public bool IsEstimated { get; init; }

    /// <summary>
    /// Gets how many reported items are not part of <c>Amount</c>: items with no
    /// amount, and items in another currency than <c>Currency</c>.
    /// </summary>
    /// <remarks>
    /// A quota or budget cannot count such an item. The count is what keeps that gap
    /// visible instead of reading as a smaller bill.
    /// </remarks>
    public int UnsummedItemCount { get; init; }
}
