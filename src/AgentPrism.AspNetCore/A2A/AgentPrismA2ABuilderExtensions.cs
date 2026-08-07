using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// AgentPrism agent'larini A2A uzerinden yayimlamak icin servis kaydini yapan
/// uzantilar.
/// </summary>
public static class AgentPrismA2ABuilderExtensions
{
    /// <summary>
    /// <see cref="AgentPrismA2AOptions.ExposedAgents"/>'teki her agent icin bir
    /// A2A sunucusu kaydeder. HTTP ucu ayrica <c>app.MapAgentPrismA2A(...)</c> ile
    /// baglanmalidir.
    /// </summary>
    /// <param name="builder">AgentPrism yapilandirma zinciri.</param>
    /// <param name="configure">Ayar degistirici.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <para>
    /// 🚨 <c>ExposedAgents</c> BURADA, kayit zamaninda okunur ve SABITLENIR.
    /// <c>AddA2AServer</c> bir agent ORNEGI ister; AgentPrism'in katalogu
    /// calisma aninda degisebildigi icin (K-019, MAF'in kendi kayit defteri
    /// KULLANILMIYOR) her ad icin gec-cozumlu bir <see cref="ExternalAgentProxy"/>
    /// kaydedilir — gercek agent HER cagrida katalogdan cozulur, ama HANGI
    /// adlarin A2A'da var oldugu kayit ANINDA sabitlenir (bolum 50.5).
    /// </para>
    /// <para>Varsayilan olarak hicbir agent disa acik degildir (K1).</para>
    /// </remarks>
    public static IAgentPrismBuilder UseA2A(
        this IAgentPrismBuilder builder,
        Action<AgentPrismA2AOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        var options = new AgentPrismA2AOptions();
        configure?.Invoke(options);

        // Deger BURADA (kayit zamaninda) DI'a konur; MapAgentPrismA2A() onu
        // Build() sonrasi geri okur. AgentPrismMcpServerOptions'tan farkli
        // olarak IOptionsMonitor DEGIL duz bir singleton'dir: A2A'nin listesi
        // zaten sabittir, IOptionsMonitor'in "calisma aninda degisebilir"
        // vaadi burada karsiliksizdir.
        services.AddSingleton(options);

        foreach (var agentName in options.ExposedAgents)
        {
            var proxy = new ExternalAgentProxy(agentName) { BudgetTemplate = options.Budget };

            services.AddKeyedSingleton(agentName, proxy);

            services.AddA2AServer(proxy, register =>
            {
                // Onay gerektiren bir arada askiya alma MAF'ta yoktur (K-103).
                // Arka plan modu bu askiya almanin baska bir bicimidir; ayni
                // sinirdan kacinmak icin acikca kapatilir.
                //
                // MEAI001: AgentRunMode "degerlendirme amaclidir" isaretli.
                // Kullanim bu TEK dosyada toplanir (MAAI001 deseniyle ayni).
#pragma warning disable MEAI001
                register.AgentRunMode = AgentRunMode.DisallowBackground;
#pragma warning restore MEAI001
            });
        }

        return builder;
    }
}
