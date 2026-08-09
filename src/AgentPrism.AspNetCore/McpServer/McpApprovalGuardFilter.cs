using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// MCP uzerinden disa acik agent'larin onay gerektiren bir tool tasimadigini
/// dogrulayan, istek isleme icinde calisan guard.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Bu denetim onceden <c>MapAgentPrismMcpServer</c> icinde, uc baglama
/// aninda (migration'lar baslamadan ONCE) senkron calisiyordu ve bos bir
/// veritabaninda katalog sorgusu "no such table" ile cokuyordu. Denetim
/// BURAYA tasindi.
/// </para>
/// <para>
/// Kontrol, bu filtre orneklendiginde (Map* aninda) BASLAYAN, uygulama omru
/// boyunca yasayan TEK bir arka plan <see cref="Task"/>'tir — <see cref="SchemaReadyGate"/>'i
/// bekler, sonra katalogu okur ve dogrular. Her istek AYNI Task'i bekler: kontrol
/// henuz bitmediyse istek onu bekler, bittiyse (basarili veya basarisiz) sonucu
/// aninda alir. Boylece hicbir istek kontrolun ONUNE gecemez.
/// </para>
/// <para>
/// 🚨 <c>IHostedService</c> DEGIL, BILEREK: bir <c>IHostedService.StartAsync</c>
/// senkron olarak <see cref="SchemaReadyGate"/> beklerse ve tuketici
/// <c>UseMcpServer()</c>'i <c>UseSqlite()</c>/<c>UsePostgreSql()</c>/<c>UseSqlServer()</c>'DAN
/// ONCE cagirirsa, genel Host'un sirali <c>IHostedService</c> baslatma dongusu
/// SESSIZCE SONSUZA DEK KILITLENIR (migration hic calismaz, guard onu hic
/// bekleyemez). K-251'in "<c>IServiceCollection</c> kurulum aninda
/// sira-bagimsizdir" ilkesi burada da gecerlidir; bir arka plan Task bu
/// kisitlamayi tasimaz.
/// </para>
/// <para>
/// Kontrol basarisiz olursa <see cref="IHostApplicationLifetime.StopApplication"/>
/// cagrilir — trafik hic gelmese bile uygulama kendini durdurur, boylece
/// bugunku "yanlis yapilandirmayla uygulama hic ayaga kalkmaz" sozlesmesi ruhen
/// korunur (yalniz zamanlamasi degisir: acilista degil, kontrol tamamlaninca).
/// </para>
/// </remarks>
internal sealed class McpApprovalGuardFilter : IEndpointFilter
{
    private readonly Task _checkTask;

    public McpApprovalGuardFilter(
        SchemaReadyGate schemaReadyGate,
        IAgentCatalog catalog,
        IToolRegistry toolRegistry,
        IOptionsMonitor<AgentPrismMcpServerOptions> optionsMonitor,
        IHostApplicationLifetime lifetime,
        ILogger<McpApprovalGuardFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(schemaReadyGate);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _checkTask = RunCheckAsync(schemaReadyGate, catalog, toolRegistry, optionsMonitor, lifetime, logger);
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        await _checkTask.ConfigureAwait(false);

        return await next(context).ConfigureAwait(false);
    }

    private static async Task RunCheckAsync(
        SchemaReadyGate schemaReadyGate,
        IAgentCatalog catalog,
        IToolRegistry toolRegistry,
        IOptionsMonitor<AgentPrismMcpServerOptions> optionsMonitor,
        IHostApplicationLifetime lifetime,
        ILogger logger)
    {
        try
        {
            await schemaReadyGate.WaitAsync(lifetime.ApplicationStopping).ConfigureAwait(false);

            var options = optionsMonitor.CurrentValue;
            var descriptors = await catalog.ListAsync(lifetime.ApplicationStopping).ConfigureAwait(false);

            ExternalSurfaceGuard.EnsureNoApprovalRequiredTools(
                descriptors, options.ExposedAgents, options.ExposeAllAgents, toolRegistry, "MCP");
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Uygulama kapaniyor; denetimin sonucu artik onemsiz
            // (McpDiscoveryService ile ayni desen).
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "MCP disa acik yuzey denetimi basarisiz oldu; uygulama durduruluyor.");

            // 🚨 StopApplication() BURADA DOGRUDAN cagrilmaz: bu kontrol Host
            // henuz kendi IHostedService baslatma dongusunu surdururken
            // tamamlanabilir (SchemaReadyGate hemen acilirsa, ornegin SQL
            // saglayicisi hic kayitli degilse). O anda StopApplication()
            // cagirmak Host.StartAsync'in KENDI iptal denetimini tetikler ve
            // onu OperationCanceledException ile PATLATIR — istekten once
            // firlamasi gereken InvalidOperationException yerine kafa
            // karistirici bir hata gorunur. ApplicationStarted, Host.StartAsync
            // GERCEKTEN tamamlanana kadar isaretlenmez; kayit zaten tamamlanmissa
            // geri cagri HEMEN calisir (CancellationToken.Register sozlesmesi).
            lifetime.ApplicationStarted.Register(lifetime.StopApplication);

            throw;
        }
    }
}
