namespace AgentPrism;

/// <summary>
/// Kiraci kavrami tasimayan bir depo metodunu kiraci yalitimi kapsam
/// denetiminden muaf tutar.
/// </summary>
/// <remarks>
/// <para>
/// Faz 41'de eklendi. <c>TenantCoverageTests</c>, paylasilan depo katmanindaki
/// her public metodun ya kiraci yalitimi sozlesmesinde sinandigini ya da bu
/// oznitelikle <strong>gerekcesi yazilarak</strong> muaf tutuldugunu dogrular.
/// Boylece yarin eklenen bir metot sessizce testsiz kalamaz.
/// </para>
/// <para>
/// 🚨 Oznitelik <c>internal</c>'dir ve public sozlesmeyi buyutmez. Yansimayi
/// yalnizca test projesi kullanir; urun kodu bu tipi hicbir zaman okumaz ve
/// AOT durusu etkilenmez.
/// </para>
/// </remarks>
/// <param name="reason">
/// Metodun neden kiraci filtrelemedigi. Bos birakilamaz; kod incelemesinde
/// gorunur olmasi bu oznitelik ile liste dosyasi arasindaki farktir.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TenantAgnosticAttribute(string reason) : Attribute
{
    /// <summary>Muafiyetin gerekcesi.</summary>
    public string Reason { get; } = reason;
}
