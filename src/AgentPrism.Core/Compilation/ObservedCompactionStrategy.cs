using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="CompactionStrategy"/>'yi sarar ve gerceklestigi her
/// sikistirmayi <see cref="RunEventType.HistoryCompacted"/> olayi olarak
/// calistirma akisina yazar.
/// </summary>
/// <remarks>
/// <para>
/// Tekdüzedir: hangi ic strateji kullanilirsa kullanilsin (SlidingWindow,
/// Summarization, Pipeline, ...) ayni gozlem burada yapilir; strateji basina
/// tekrar yazilmaz.
/// </para>
/// <para>
/// Ic strateji, tetikleyicisi <em>her zaman dogru</em> olacak sekilde kurulur
/// (bkz. <c>AgentDefinitionCompiler.BuildCompactionStrategy</c>): gercek
/// tetikleyici koşulunu yalniz bu dis sarmalayici tasir. Ic stratejinin
/// korumali <c>CompactCoreAsync</c>'ine erisilemez — C#'ta korumali bir uye,
/// bildiren tipin kendi turunden olmayan bir kardes ornek uzerinden
/// cagrilamaz. Bu yuzden ic strateji, kendi tetikleyicisini zaten gecmis
/// sayarak calisan public ve sanal-olmayan <c>CompactAsync</c> ile cagrilir.
/// </para>
/// </remarks>
// MAAI001: Microsoft.Agents.AI.Compaction.* "evaluation purposes only" olarak
// isaretli. Bu dosyanin tamami bu API'yle calistigi icin bastirma sinif
// govdesini kapsar; MAF bu API'yi degistirirse yalniz bu dosya guncellenir.
// Gerekce: docs/KARARLAR.md (K-020 ile ayni desen).
#pragma warning disable MAAI001
internal sealed class ObservedCompactionStrategy : CompactionStrategy
{
    /// <summary>Yeni bir gozlemlenen strateji olusturur.</summary>
    /// <param name="inner">Sarilan gercek strateji.</param>
    /// <param name="trigger">Gercek kullanici kosulunu tasiyan tetikleyici.</param>
    /// <param name="target">Hedef tetikleyici. Bu fazda kullanilmiyor, <see langword="null"/> gecilir.</param>
    internal ObservedCompactionStrategy(CompactionStrategy inner, CompactionTrigger trigger, CompactionTrigger? target)
        : base(trigger, target)
    {
        ArgumentNullException.ThrowIfNull(inner);

        Inner = inner;
    }

    /// <summary>Sarilan gercek strateji. Testlerde yapisal dogrulama icin kullanilir.</summary>
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

        var writer = AgentPrismRunContext.Current?.Writer;

        if (writer is null)
        {
            return changed;
        }

        var afterMessages = index.IncludedMessageCount;
        var afterTokens = index.IncludedTokenCount;

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.HistoryCompacted)
            {
                Text = $"{Math.Max(beforeMessages - afterMessages, 0)} mesaj ozetlendi",
                Payload = $"beforeMessages={beforeMessages}, afterMessages={afterMessages}, " +
                          $"beforeTokens={beforeTokens}, afterTokens={afterTokens}",
            },
            cancellationToken).ConfigureAwait(false);

        return changed;
    }
}
#pragma warning restore MAAI001
