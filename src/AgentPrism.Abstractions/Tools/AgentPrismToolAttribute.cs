namespace AgentPrism;

/// <summary>
/// Bir metodu tool olarak isaretler. <c>AddToolsFrom&lt;T&gt;()</c> yalnizca bu
/// oznitelige sahip metotlari kaydeder.
/// </summary>
/// <remarks>
/// <para>
/// Isaretleme <strong>acik tercihtir</strong>. Bir sinifa yeni bir public metot
/// eklemek onu kendiliginden agent'lara acmaz. Bu, tasarim kurali K2'nin
/// (tool'lar yalnizca kodda tanimlanir) dogal devamidir.
/// </para>
/// <para>
/// Aciklama alani modelin tool'u <em>ne zaman</em> cagiracagini anlatir; bos
/// birakilirsa model yalnizca ada ve parametre semasina bakar.
/// </para>
/// <example>
/// <code>
/// internal static class OrderTools
/// {
///     [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
///     public static string GetOrderStatus(string orderId) =&gt; ...;
///
///     public static void Helper() { }   // tool olmaz
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AgentPrismToolAttribute : Attribute
{
    /// <summary>Metot adini tool adi olarak kullanan bir isaret olusturur.</summary>
    public AgentPrismToolAttribute()
    {
    }

    /// <summary>Tool adini acikca veren bir isaret olusturur.</summary>
    /// <param name="name">Tool adi. Agent tanimlarinda bu ad kullanilir.</param>
    public AgentPrismToolAttribute(string name) => Name = name;

    /// <summary>Ad ve aciklama veren bir isaret olusturur.</summary>
    /// <param name="name">Tool adi. Agent tanimlarinda bu ad kullanilir.</param>
    /// <param name="description">Modelin tool'u ne zaman cagiracagini anlatan aciklama.</param>
    public AgentPrismToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Tool adi. <see langword="null"/> ise metot adi kullanilir.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Modelin tool'u ne zaman cagiracagini anlatan aciklama.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Cagri oncesi acik onay gerekip gerekmedigi.
    /// </summary>
    /// <remarks>
    /// Isaretli tool, defterde <c>ApprovalRequiredAIFunction</c> ile sarilir.
    /// Microsoft Agent Framework tool'u calistirmak yerine bir onay istegi
    /// uretir; cagri kullanicinin karari gelene kadar bekler. Geri alinamaz is
    /// yapan tool'lari (iptal, silme, odeme) isaretleyin.
    /// </remarks>
    public bool RequiresApproval { get; init; }
}
