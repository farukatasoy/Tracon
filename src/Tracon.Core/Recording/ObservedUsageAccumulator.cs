using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Adds up the token usage of every response the provider billed to the agent's own model during one run,
/// at the moment the response arrives.
/// </summary>
/// <remarks>
/// <para>
/// The run recorder reads a run's usage from its answer, and a run that ends in an exception or a cancellation has
/// no answer: the provider had already billed the model calls that came before the failure, and the answer that
/// would have carried their total was never built. This accumulator keeps what <see cref="ObservedUsageChatClient"/>
/// saw on the way past, so the run's row, its cost and its quota entry can say what the run really spent.
/// </para>
/// <para>
/// It counts the agent's own model only. The calls a run makes to another model outside the agent's turns (the
/// compaction summarizer, an <c>aiJudge</c> loop judge) are the share of <see cref="SideChannelUsageAccumulator"/>,
/// priced at the model that answered them, and a sub-agent's run has an accumulator of its own on its own row.
/// </para>
/// <para>
/// A run that finishes with an answer keeps the usage of that answer. The two agree: the answer's usage is the
/// total of the same calls this accumulator saw.
/// </para>
/// </remarks>
internal sealed class ObservedUsageAccumulator
{
    private readonly UsageTally _tally = new();

    /// <summary>Adds the usage of one model response, or of one part of a streamed response.</summary>
    /// <param name="usage">The usage the provider reported. Ignores <see langword="null"/>.</param>
    public void Add(UsageDetails? usage) => _tally.Add(usage);

    /// <summary>Converts what was observed to <see cref="RunUsage"/>.</summary>
    /// <returns><see langword="null"/> when no response reported any usage: nothing observed is not a measured zero.</returns>
    public RunUsage? ToRunUsage() => _tally.ToRunUsage();
}
