namespace AgentPrism;

/// <summary>Skill script calistirma izinlerinin deposu.</summary>
public interface ISkillScriptGrantStore
{
   /// <summary>Kiracinin tum izin kayitlarini listeler.</summary>
   /// <param name="tenantId">Kiraci kimligi.</param>
   /// <param name="cancellationToken">Iptal belirteci.</param>
   /// <returns>Geri alinmis ve suresi dolmus kayitlar da dahil tum izinler.</returns>
   ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
       string tenantId,
       CancellationToken cancellationToken = default);

   /// <summary>
   /// Verilen script icin yururlukteki izni bulur.
   /// </summary>
   /// <param name="tenantId">Kiraci kimligi.</param>
   /// <param name="skillName">Skill adi.</param>
   /// <param name="scriptName">Script adi.</param>
   /// <param name="instant">Sure denetiminin yapilacagi an.</param>
   /// <param name="cancellationToken">Iptal belirteci.</param>
   /// <returns>
   /// Once script'e ozgu izin, yoksa skill'in tumunu kapsayan izin. Yururlukte
   /// izin yoksa <see langword="null"/>.
   /// </returns>
   ValueTask<SkillScriptGrant?> FindActiveAsync(
       string tenantId,
       string skillName,
       string scriptName,
       DateTimeOffset instant,
       CancellationToken cancellationToken = default);

   /// <summary>Izin verir veya var olan izni gunceller.</summary>
   /// <param name="grant">Kaydedilecek izin.</param>
   /// <param name="cancellationToken">Iptal belirteci.</param>
   /// <returns>Kimligi ve zaman damgasi atanmis izin.</returns>
   ValueTask<SkillScriptGrant> GrantAsync(
       SkillScriptGrant grant,
       CancellationToken cancellationToken = default);

   /// <summary>Izni geri alir.</summary>
   /// <param name="tenantId">Kiraci kimligi.</param>
   /// <param name="skillName">Skill adi.</param>
   /// <param name="scriptName">Script adi; <see langword="null"/> ise skill genelindeki izin.</param>
   /// <param name="cancellationToken">Iptal belirteci.</param>
   /// <returns>Yururlukteki bir izin geri alindiysa <see langword="true"/>.</returns>
   ValueTask<bool> RevokeAsync(
       string tenantId,
       string skillName,
       string? scriptName,
       CancellationToken cancellationToken = default);
}
