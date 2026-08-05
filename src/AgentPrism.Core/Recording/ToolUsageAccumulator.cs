using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Bir calistirma icinde tool'larin bildirdigi token disi olcumleri, cagri
/// kimligine gore <see cref="ToolInvocationTracker"/>'a tasir.
/// </summary>
/// <remarks>
/// <para>
/// Tool bir <c>AIFunction</c> govdesinde calisir ve kendi kaydini yazamaz; kayit
/// <c>ToolInvoking</c>/<c>ToolInvoked</c> olay ciftinden ureti­lir. Bildirilen
/// olcumu dogru kayda baglayan tek anahtar <strong>cagri kimligidir</strong>.
/// </para>
/// <para>
/// Sozluk eszamanlidir: <c>FunctionInvokingChatClient.AllowConcurrentInvocation</c>
/// acildiginda ayni calistirmada birden cok tool ayni anda calisir.
/// </para>
/// <para>
/// Bildirilen ama hicbir sonuca eslenmeyen bir olcum (cagri iptal edildi, sonuc
/// hic gelmedi) sozlukte kalir ve calistirmayla birlikte atilir. Bu bir sizinti
/// degildir: nesnenin omru calistirmanin omrudur.
/// </para>
/// </remarks>
internal sealed class ToolUsageAccumulator
{
    private readonly ConcurrentDictionary<string, ToolCallUsage> _byCallId = new(StringComparer.Ordinal);

    /// <summary>Bir cagrinin olcumunu kaydeder.</summary>
    /// <param name="callId">Modelin urettigi cagri kimligi.</param>
    /// <param name="usage">Olcum.</param>
    /// <remarks>
    /// Ayni kimlik ikinci kez bildirilirse son deger kazanir: bir tool kendi
    /// icinde birden cok saglayici cagrisi yaparsa toplami en sonda bildirir.
    /// </remarks>
    public void Report(string callId, ToolCallUsage usage) => _byCallId[callId] = usage;

    /// <summary>Bir cagrinin olcumunu alir ve sozlukten cikarir.</summary>
    /// <param name="callId">Cagri kimligi.</param>
    /// <returns>Bildirilmis olcum; yoksa <see langword="null"/>.</returns>
    public ToolCallUsage? Take(string callId)
        => _byCallId.TryRemove(callId, out var usage) ? usage : null;
}
