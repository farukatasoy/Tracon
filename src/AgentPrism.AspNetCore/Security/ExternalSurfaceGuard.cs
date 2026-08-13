using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// MCP ve A2A dis yuzeylerinin paylastigi acilis denetimleri (Faz 50).
/// </summary>
/// <remarks>
/// Amac K1'in ayni uygulamasi: sessizce yarim calisan bir dis yuzey yerine
/// acik bir hata. <see cref="EnsureRemoteAccessNotCombined"/>
/// <c>MapAgentPrismMcpServer</c>/<c>MapAgentPrismA2A</c> cagrisi aninda (uc
/// baglama, <c>app.Run()</c>'dan ONCE) calisir; DB'ye dokunmaz. Onay guard'i
/// (<see cref="EnsureNoApprovalRequiredTools"/>) ise DB'ye dokunur ve bu yuzden
/// <c>McpApprovalGuardFilter</c>/<c>A2AApprovalGuardFilter</c> icinden, uc
/// baglama aninda BASLAYAN ama sema hazir olana kadar arka planda bekleyen bir
/// Task icinde calisir — bos bir veritabaninda "no such table" ile cokmemek
/// icin (K-354'un ayni deseni). Bu Task her istekten ONCE beklenir; hicbir
/// istek denetimin onune gecemez.
/// </remarks>
internal static class ExternalSurfaceGuard
{
    private static readonly TimeSpan MaxCatalogListDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Katalogu, sema henuz sorgulanabilir olmayabilecegi icin uygulama kapanana
    /// kadar yeniden deneyerek okur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 HATA-S1-002: <c>AutoApplyMigrations=false</c> iken
    /// <see cref="SchemaReadyGate.MarkReady"/> semanin GERCEKTEN sorgulanabilir
    /// oldugunu dogrulamadan cagrilir (semanin hazirlanmasi tuketicinin
    /// sorumlulugudur — bkz. <see cref="SchemaReadyGate.MarkReady"/>'nin kendi
    /// notu). Semayi kuran harici bir gecis adimi uygulama baslarken henuz
    /// tamamlanmamis olabilir, hatta uygulama omru boyunca hic calismayabilir
    /// (operator migration'i elle, sonra calistirmayi secebilir). Boyle bir depo
    /// hatasi calistirmayi HEMEN KESMEMELIDIR (K1, "depo hatasi calistirmayi
    /// kesmez") — <c>MT-SQL-004</c>'un kendi sozlesmesi de aynisini ister:
    /// uygulama ACIK kalir, <c>/health</c> <c>Unhealthy</c> bildirir. Bu yuzden
    /// deneme SAYISI degil, yalnizca <paramref name="lifetime"/>'in
    /// <see cref="IHostApplicationLifetime.ApplicationStopping"/>'i sinirlar —
    /// sema hic hazirlanmazsa yalniz A2A/MCP disa acik yuzeyine gelen istekler
    /// (uygulama kapanana kadar) bu Task'i bekler; uygulamanin geri kalani
    /// (agent CRUD, saglik ucu, vb.) hemen kullanilabilir kalir ve HICBIR ZAMAN
    /// <see cref="IHostApplicationLifetime.StopApplication"/> ile durdurulmaz.
    /// </para>
    /// </remarks>
    /// <param name="catalog">Sorgulanacak katalog.</param>
    /// <param name="lifetime">Uygulama omru — deneme araligi ve iptali bundan okunur.</param>
    /// <param name="logger">Ara denemelerin uyari olarak yazildigi logger.</param>
    /// <param name="protocol">Log mesajinda gorunen dis yuzey adi (<c>"A2A"</c>/<c>"MCP"</c>).</param>
    /// <returns>Katalog ozetleri.</returns>
    public static async Task<IReadOnlyList<AgentDescriptor>> ListCatalogWithRetryAsync(
        IAgentCatalog catalog,
        IHostApplicationLifetime lifetime,
        ILogger logger,
        string protocol)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await catalog.ListAsync(lifetime.ApplicationStopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "{Protocol} disa acik yuzey denetimi icin katalog sorgusu {Attempt}. denemede " +
                    "basarisiz oldu; sema henuz sorgulanabilir olmayabilir (AutoApplyMigrations=false " +
                    "ile harici bir gecis adimi bekleniyor olabilir). {DelayMilliseconds} ms sonra " +
                    "yeniden denenecek; bu arada uygulamanin geri kalani calisir durumda kalir.",
                    protocol,
                    attempt,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, lifetime.ApplicationStopping).ConfigureAwait(false);

                var next = delay * 2;
                delay = next < MaxCatalogListDelay ? next : MaxCatalogListDelay;
            }
        }
    }

    /// <summary>Bir agentin beyaz listede olup olmadigini soyler.</summary>
    /// <param name="agentName">Denetlenecek agent adi.</param>
    /// <param name="exposedAgents">Beyaz liste.</param>
    /// <param name="exposeAll">Tum agent'lar acik mi.</param>
    /// <returns>Agent disa aciksa <see langword="true"/>.</returns>
    public static bool IsExposed(string agentName, ICollection<string> exposedAgents, bool exposeAll)
        => exposeAll || exposedAgents.Contains(agentName, StringComparer.Ordinal);

    /// <summary>
    /// <c>AllowRemoteAccess</c> acikken, sistemde en az bir gecerli
    /// <see cref="ApiKeyScope.ExternalInvoke"/> kapsamli anahtar yoksa bir dis
    /// yuzeyin acilmasini engeller.
    /// </summary>
    /// <param name="allowRemoteAccess"><see cref="AgentPrismEndpointOptions.AllowRemoteAccess"/> degeri.</param>
    /// <param name="protocol">Acilan dis yuzeyin adi.</param>
    /// <param name="apiKeyStore">Anahtar deposu.</param>
    /// <exception cref="InvalidOperationException">
    /// Uzak erisim acikken ve gecerli bir <c>external:invoke</c> anahtari yokken cagrilmissa.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Faz 53 (bolum 53.4) Faz 50'nin kilidini KOSULLANDIRIR, KALDIRMAZ: tek
    /// statik bearer token loopback disina acilmis bir agent yuzeyini
    /// korumaya yetmez, ama kiraci bazli, <c>external:invoke</c> kapsamli bir
    /// API anahtari yeterlidir.
    /// </para>
    /// <para>
    /// 🚨 Senkron cagri BILEREK yapilir: bu denetim acilista, istek isleme
    /// disinda bir kez calisir (<see cref="AgentPrismMcpServerExtensions.MapAgentPrismMcpServer"/>
    /// ile ayni gerekce).
    /// </para>
    /// </remarks>
    public static void EnsureRemoteAccessNotCombined(bool allowRemoteAccess, string protocol, IApiKeyStore apiKeyStore)
    {
        ArgumentNullException.ThrowIfNull(apiKeyStore);

        if (!allowRemoteAccess)
        {
            return;
        }

        var hasExternalInvokeKey = apiKeyStore
            .HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        if (hasExternalInvokeKey)
        {
            return;
        }

        throw new InvalidOperationException(
            $"AllowRemoteAccess acikken {protocol} disa acilamaz: sistemde 'external:invoke' " +
            "kapsamli, suresi gecmemis ve iptal edilmemis bir API anahtari yok. Tek statik bearer " +
            "token, loopback disina acilmis bir agent yuzeyini korumaya yetmez. " +
            "'POST /api/api-keys' ile 'external:invoke' kapsamli bir anahtar uretin.");
    }

    /// <summary>
    /// Disa acilacak agent'lardan hicbirinin onay gerektiren bir tool tasimadigini
    /// dogrular.
    /// </summary>
    /// <param name="descriptors">Katalogdaki agent ozetleri.</param>
    /// <param name="exposedAgents">Beyaz liste.</param>
    /// <param name="exposeAll">Tum agent'lar acik mi.</param>
    /// <param name="toolRegistry">Onay bayragini tasiyan tool defteri.</param>
    /// <param name="protocol">Acilan dis yuzeyin adi.</param>
    /// <exception cref="InvalidOperationException">
    /// Disa acik bir agent onay gerektiren bir tool tasiyorsa.
    /// </exception>
    /// <remarks>
    /// K-103'un ayni sinirini acilista zorlar: dis cagiran bir insan degildir ve
    /// onay isteğine cevap veremez. Sessizce onaysiz calistirmak kabul edilemez.
    /// </remarks>
    public static void EnsureNoApprovalRequiredTools(
        IReadOnlyList<AgentDescriptor> descriptors,
        ICollection<string> exposedAgents,
        bool exposeAll,
        IToolRegistry toolRegistry,
        string protocol)
    {
        var approvalRequiredToolNames = toolRegistry.List()
            .Where(static tool => tool.RequiresApproval)
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (approvalRequiredToolNames.Count == 0)
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            if (!IsExposed(descriptor.Name, exposedAgents, exposeAll))
            {
                continue;
            }

            var offending = descriptor.ToolNames.Where(approvalRequiredToolNames.Contains).ToList();

            if (offending.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"'{descriptor.Name}' agent'i {protocol} uzerinden disa acilamaz: " +
                $"'{string.Join(", ", offending)}' tool'lari kullanici onayi istiyor. " +
                "Dis cagiran bir agent degildir ve onay isteğine cevap veremez. Bu agent'i " +
                "beyaz listeden cikarin veya onay gerektirmeyen tool'larla sinirlayin.");
        }
    }
}
