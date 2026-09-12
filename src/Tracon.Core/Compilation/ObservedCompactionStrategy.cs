using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Wraps a <see cref="CompactionStrategy"/> and records every completed
/// compaction in the run stream as a <see cref="RunEventType.HistoryCompacted"/> event.
/// </summary>
/// <remarks>
/// <para>
/// This is uniform: regardless of the inner strategy, such as SlidingWindow,
/// Summarization, or Pipeline, observation occurs here and is not repeated per strategy.
/// </para>
/// <para>
/// The inner strategy uses an <em>always true</em> trigger (see
/// <c>AgentDefinitionCompiler.BuildCompactionStrategy</c>). This outer wrapper
/// alone owns the real trigger condition. The inner strategy's protected
/// <c>CompactCoreAsync</c> cannot be accessed: C# does not permit a protected
/// member through a sibling instance that is not the declaring type. Therefore,
/// the inner strategy is invoked through public, non-virtual <c>CompactAsync</c>,
/// which treats its own trigger as already satisfied.
/// </para>
/// </remarks>
// MAAI001: Microsoft.Agents.AI.Compaction.* is marked "evaluation purposes only".
// The suppression covers the class body because the complete file uses this API.
// If MAF changes the API, only this file needs updating. Rationale:
// docs/KARARLAR.md (the same pattern as K-020).
#pragma warning disable MAAI001
internal sealed class ObservedCompactionStrategy : CompactionStrategy
{
    /// <summary>Initializes a new observed strategy.</summary>
    /// <param name="inner">The wrapped strategy.</param>
    /// <param name="trigger">The trigger that carries the actual user condition.</param>
    /// <param name="target">The target trigger. It is unused and receives <see langword="null"/>.</param>
    internal ObservedCompactionStrategy(CompactionStrategy inner, CompactionTrigger trigger, CompactionTrigger? target)
        : base(trigger, target)
    {
        ArgumentNullException.ThrowIfNull(inner);

        Inner = inner;
    }

    /// <summary>Gets the wrapped strategy. Tests use it for structural verification.</summary>
    internal CompactionStrategy Inner { get; }

    /// <inheritdoc />
    protected override async ValueTask<bool> CompactCoreAsync(
        CompactionMessageIndex index,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var beforeMessages = index.IncludedMessageCount;
        var beforeTokens = index.IncludedTokenCount;

        var changed = await Inner.CompactAsync(index, logger, cancellationToken).ConfigureAwait(false);

        if (!changed)
        {
            return changed;
        }

        var writer = TraconRunContext.Current?.Writer;

        if (writer is null)
        {
            return changed;
        }

        var afterMessages = index.IncludedMessageCount;
        var afterTokens = index.IncludedTokenCount;

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.HistoryCompacted)
            {
                Text = $"{Math.Max(beforeMessages - afterMessages, 0)} messages compacted",
                Payload = $"beforeMessages={beforeMessages}, afterMessages={afterMessages}, " +
                          $"beforeTokens={beforeTokens}, afterTokens={afterTokens}",
            },
            cancellationToken).ConfigureAwait(false);

        return changed;
    }
}
#pragma warning restore MAAI001
