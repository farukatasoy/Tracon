namespace AgentPrism;

/// <summary>AgentPrism agent'larini MCP tool'u olarak yayimlama ayarlari.</summary>
/// <remarks>
/// Bu tip bilerek <c>record</c> <strong>degildir</strong> (K-035 deseni): ayar
/// nesnesi <c>secret</c> tasimaz ama derleyicinin uretecegi <c>ToString</c> ileride
/// eklenecek bir alanda ayni tuzagi tekrar edebilir; sinif tip disiplini korunur.
/// </remarks>
public sealed class AgentPrismMcpServerOptions
{
    /// <summary>
    /// Disa acilacak agent adlari. Bos ise <see cref="ExposeAllAgents"/> kapaliyken
    /// hicbir agent MCP uzerinden gorunmez.
    /// </summary>
    public IList<string> ExposedAgents { get; } = [];

    /// <summary>
    /// Katalogdaki her agent'i acar. Varsayilan <see langword="false"/>; acmak
    /// acik bir tercihtir (K1).
    /// </summary>
    public bool ExposeAllAgents { get; set; }

    /// <summary>
    /// MCP tool adi oneki. Varsayilan <c>agentprism</c>; tool adi
    /// <c>{ToolNamePrefix}_{agent}</c> olur.
    /// </summary>
    public string ToolNamePrefix { get; set; } = "agentprism";

    /// <summary>
    /// Dis cagrilar icin derinlik ve token butcesi sablonu. Her <c>tools/call</c>
    /// bu degerlerle YENI bir <see cref="AgentRunBudget"/> ornegi olusturur;
    /// nesnenin kendisi paylasilmaz.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see cref="AgentRunBudget.MaxDepth"/> degeri 1'dir: disaridan
    /// cagrilan bir agent'in kendi agacini acmasi maliyeti ongorulemez yapar.
    /// </remarks>
    public AgentRunBudget Budget { get; set; } = new() { MaxDepth = 1 };
}
