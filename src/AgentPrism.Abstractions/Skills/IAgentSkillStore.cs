namespace AgentPrism;

/// <summary>Kiraciya ait calisma ani skill tanimlarinin deposu.</summary>
public interface IAgentSkillStore
{
    /// <summary>Kiracinin tum skill'lerini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Adina gore siralanmis skill'ler.</returns>
    ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Kiracida verilen adla eslesen skill'i getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Skill adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Skill; yoksa <see langword="null"/>.</returns>
    ValueTask<AgentSkillDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Skill'i olusturur veya gunceller.</summary>
    /// <param name="skill">Kaydedilecek skill.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kimlik, zaman damgalari ve surumu atanmis skill.</returns>
    ValueTask<AgentSkillDefinition> SaveAsync(
        AgentSkillDefinition skill,
        CancellationToken cancellationToken = default);

    /// <summary>Kiracinin skill'ini siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Skill adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Skill silindiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);
}
