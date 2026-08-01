using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Kodda tanimlanmis bir agent kaydi. Iki bicimden biri kullanilir:
/// bildirimsel <see cref="Definition"/> veya tam denetim veren <see cref="Factory"/>.
/// </summary>
public sealed class CodeAgentRegistration
{
    private CodeAgentRegistration(string name)
    {
        Name = name;
    }

    /// <summary>Agent adi.</summary>
    public string Name { get; }

    /// <summary>Bildirimsel tanim. <see cref="Factory"/> kullanildiysa <see langword="null"/>.</summary>
    public AgentDefinition? Definition { get; private init; }

    /// <summary>Agent'i ureten fabrika. <see cref="Definition"/> kullanildiysa <see langword="null"/>.</summary>
    public Func<IServiceProvider, AIAgent>? Factory { get; private init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; private init; }

    /// <summary>Kisa aciklama.</summary>
    public string? Description { get; private init; }

    /// <summary>
    /// Bildirimsel bir kayit olusturur. Tanim, AgentPrism derleyicisinden gecer;
    /// tool ve model dogrulamasi uygulanir.
    /// </summary>
    /// <param name="definition">Agent tanimi.</param>
    /// <returns>Kayit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    public static CodeAgentRegistration FromDefinition(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new CodeAgentRegistration(definition.Name)
        {
            Definition = definition with { Origin = AgentDefinitionOrigin.Code, Version = 1 },
            DisplayName = definition.DisplayName,
            Description = definition.Description,
        };
    }

    /// <summary>
    /// Fabrika tabanli bir kayit olusturur. Agent'in nasil kuruldugu tamamen
    /// cagirana aittir; AgentPrism yalnizca katalogda gosterir ve calistirma
    /// kaydi ile sarar.
    /// </summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="factory">Agent'i ureten fabrika.</param>
    /// <param name="description">Kisa aciklama.</param>
    /// <param name="displayName">Arayuzde gosterilecek ad.</param>
    /// <returns>Kayit.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> bos ise.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> <see langword="null"/> ise.</exception>
    public static CodeAgentRegistration FromFactory(
        string name,
        Func<IServiceProvider, AIAgent> factory,
        string? description = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        return new CodeAgentRegistration(name)
        {
            Factory = factory,
            Description = description,
            DisplayName = displayName,
        };
    }
}
