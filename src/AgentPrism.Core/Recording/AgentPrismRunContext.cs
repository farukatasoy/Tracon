namespace AgentPrism;

/// <summary>
/// Suren calistirmanin kimligini, calistirma yolunun icindeki yardimci
/// bilesenlere tasir.
/// </summary>
/// <remarks>
/// <para>
/// Skill script calistirmasi MAF'in icinden, kayit sarmalayicisinin
/// <em>altinda</em> tetiklenir. Calistirma kimligini oraya parametre olarak
/// gecirmenin yolu yoktur: cagri zinciri MAF'a aittir.
/// </para>
/// <para>
/// 🚨 Deger bir <see cref="AsyncLocal{T}"/> icinde tutulur. Bu, atamanin
/// <strong>cagirana geri akmadigi</strong> anlamina gelir: <c>Set</c> cagrisi
/// calistirmayi baslatan metodun <em>kendi govdesinde</em> yapilmalidir.
/// Ayni tuzak <see cref="System.Diagnostics.Activity.Current"/> ile Faz 6'da
/// yasandi.
/// </para>
/// </remarks>
public static class AgentPrismRunContext
{
   private static readonly AsyncLocal<Guid?> CurrentHolder = new();

   /// <summary>Suren calistirmanin kimligi. Calistirma disinda <see langword="null"/>.</summary>
   public static Guid? CurrentRunId => CurrentHolder.Value;

   /// <summary>Suren calistirmanin kimligini ayarlar.</summary>
   /// <param name="runId">Calistirma kimligi.</param>
   public static void SetCurrentRunId(Guid? runId) => CurrentHolder.Value = runId;
}
