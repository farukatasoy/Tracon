namespace AgentPrism;

/// <summary>AgentPrism agent'larini A2A uzerinden yayimlama ayarlari.</summary>
/// <remarks>
/// <para>
/// 🚨 <see cref="AgentPrismMcpServerOptions.ExposeAllAgents"/> karsiligi
/// <strong>YOKTUR</strong>. Olculdu (<c>Microsoft.Agents.AI.Hosting.A2A</c>
/// 1.16.0-preview.260730.1): <c>AddA2AServer</c> bir KAYIT ZAMANI API'sidir ve
/// yalniz <see cref="ExposedAgents"/>'te acikca adi verilen agent'lari
/// yayimlayabilir; calisma aninda eklenen bir agent'i goremez (bolum 50.5).
/// </para>
/// <para>
/// Bu tip bilerek <c>record</c> <strong>degildir</strong> (K-035 deseni).
/// </para>
/// </remarks>
public sealed class AgentPrismA2AOptions
{
    /// <summary>
    /// Disa acilacak agent adlari. Acilista okunur ve SABITTIR — calisma aninda
    /// eklenen bir ad A2A'da GORUNMEZ.
    /// </summary>
    public IList<string> ExposedAgents { get; } = [];

    /// <summary>
    /// Dis cagrilar icin derinlik ve token butcesi sablonu. Her cagri bu
    /// degerlerle YENI bir <see cref="AgentRunBudget"/> ornegi olusturur;
    /// nesnenin kendisi paylasilmaz.
    /// </summary>
    public AgentRunBudget Budget { get; set; } = new() { MaxDepth = 1 };
}
