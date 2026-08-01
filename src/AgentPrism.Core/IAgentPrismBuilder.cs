using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// AgentPrism'i yapilandiran akici zincir. <c>AddAgentPrism()</c> cagrisindan doner.
/// </summary>
/// <remarks>
/// Saglayici ve depolama paketleri bu zincire kendi uzantilarini ekler:
/// <c>UsePostgreSql()</c>, <c>UseOpenAI()</c> gibi.
/// </remarks>
public interface IAgentPrismBuilder
{
    /// <summary>Alttaki servis koleksiyonu.</summary>
    IServiceCollection Services { get; }

    /// <summary>Calisma zamani ayarlarini degistirir.</summary>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    IAgentPrismBuilder Configure(Action<AgentPrismOptions> configure);

    /// <summary>Bir tool kaydeder.</summary>
    /// <param name="tool">Kaydedilecek tool.</param>
    /// <param name="requiresApproval">Cagri oncesi acik onay gerekip gerekmedigi.</param>
    /// <returns>Zincirin devami.</returns>
    IAgentPrismBuilder AddTool(AIFunction tool, bool requiresApproval = false);

    /// <summary>
    /// Bir metottan tool uretir ve kaydeder.
    /// </summary>
    /// <param name="method">Tool olarak sunulacak metot.</param>
    /// <param name="name">Tool adi. Bos birakilirsa metot adi kullanilir.</param>
    /// <param name="description">Modelin tool'u ne zaman cagiracagini anlatan aciklama.</param>
    /// <param name="requiresApproval">Cagri oncesi acik onay gerekip gerekmedigi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <remarks>
    /// Bu asiri yukleme <c>AIFunctionFactory</c> uzerinden yansima kullanir ve
    /// bu yuzden kirpma (trimming) ile native AOT senaryolarinda guvenli degildir.
    /// AOT hedefleyen uygulamalar <see cref="AddTool(AIFunction, bool)"/> asiri
    /// yuklemesini kullanmalidir.
    /// </remarks>
    [RequiresUnreferencedCode("Metottan tool uretmek yansima kullanir; kirpilmis uygulamalarda tip bilgisi kaybolabilir.")]
    [RequiresDynamicCode("Metottan tool uretmek calisma aninda kod uretimi gerektirebilir.")]
    IAgentPrismBuilder AddTool(Delegate method, string? name = null, string? description = null, bool requiresApproval = false);

    /// <summary>
    /// Kodda bildirimsel bir agent tanimlar. Tanim AgentPrism derleyicisinden gecer;
    /// model ve tool dogrulamasi uygulanir.
    /// </summary>
    /// <param name="definition">Agent tanimi.</param>
    /// <returns>Zincirin devami.</returns>
    IAgentPrismBuilder AddAgent(AgentDefinition definition);

    /// <summary>
    /// Kodda fabrika tabanli bir agent tanimlar. Agent'in nasil kuruldugu tamamen
    /// cagirana aittir.
    /// </summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="factory">Agent'i ureten fabrika.</param>
    /// <param name="description">Kisa aciklama.</param>
    /// <returns>Zincirin devami.</returns>
    IAgentPrismBuilder AddAgent(string name, Func<IServiceProvider, AIAgent> factory, string? description = null);

    /// <summary>Bir model saglayicisi kaydeder.</summary>
    /// <param name="provider">Saglayici.</param>
    /// <returns>Zincirin devami.</returns>
    IAgentPrismBuilder AddModelProvider(IModelProvider provider);

    /// <summary>Bir model saglayicisini fabrika ile kaydeder.</summary>
    /// <param name="factory">Saglayiciyi ureten fabrika.</param>
    /// <returns>Zincirin devami.</returns>
    IAgentPrismBuilder AddModelProvider(Func<IServiceProvider, IModelProvider> factory);
}
