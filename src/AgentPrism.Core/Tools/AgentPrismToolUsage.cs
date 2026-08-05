using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir tool'un kendi govdesinden token DISI olcumunu bildirmesini saglar.
/// </summary>
/// <remarks>
/// <para>
/// Ses uretimi karakterle, ses cozumu saniyeyle faturalanir; ikisi de token
/// degildir ve <c>runs</c> tablosunun maliyet sutunlarina yazilamaz. Olcum
/// suren cagrinin <see cref="ToolInvocationRecord"/> kaydina baglanir.
/// </para>
/// <para>
/// Cagri kimligi <see cref="FunctionInvokingChatClient.CurrentContext"/>'ten
/// okunur. Bu, olculerek dogrulandi (2026-08-05): <c>AIFunctionArguments.Context</c>
/// sozlugu <see langword="null"/> gelir ve cagri kimligini TASIMAZ; statik
/// baglam ise tool govdesinde doludur. Bagimliliklar ise
/// <c>AIFunctionArguments.Services</c> uzerinden cozulur.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// AgentPrismToolUsage.Report(new ToolCallUsage
/// {
///     Unit = ToolUsageUnits.Characters,
///     Quantity = audio.CharactersBilled ?? request.Text.Length,
///     Cost = price,
///     Currency = "USD",
///     IsEstimated = audio.CharactersBilled is null,
/// });
/// </code>
/// </example>
public static class AgentPrismToolUsage
{
    /// <summary>Suren tool cagrisinin olcumunu bildirir.</summary>
    /// <param name="usage">Olcum.</param>
    /// <returns>
    /// Olcum bir cagriya baglanabildiyse <see langword="true"/>. Calistirma
    /// kaydi kapaliysa veya cagri bir tool baglaminda degilse
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// Metot <strong>hicbir kosulda istisna firlatmaz</strong> (bos arguman
    /// disinda) ve <see langword="false"/> donmesi tool'un isini bozmaz.
    /// Gozlemlenebilirlik islevselligi bozmaz — bu, depo hatasinin calistirmayi
    /// kesmemesiyle ayni kuraldir.
    /// </remarks>
    public static bool Report(ToolCallUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (AgentPrismRunContext.Current?.ToolUsage is not { } accumulator)
        {
            return false;
        }

        if (FunctionInvokingChatClient.CurrentContext?.CallContent.CallId is not { Length: > 0 } callId)
        {
            return false;
        }

        accumulator.Report(callId, usage);

        return true;
    }
}
