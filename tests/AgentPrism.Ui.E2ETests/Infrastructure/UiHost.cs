using AgentPrism.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// <see cref="UiHost"/>'un sahte saglayicisindaki model adlari. Uc ayri model,
/// uc ayri bagimsiz yanit kuyrugu tasir (<see cref="FakeModelProvider.ForModel"/>);
/// aralarinda paylasilan durum yoktur.
/// </summary>
internal static class ScriptedModels
{
    /// <summary>Saglayici adi.</summary>
    public const string ProviderName = "scripted";

    /// <summary>Arayuzden olusturulan ad-hoc agent'larin kullandigi varsayilan model: yalniz yankilar.</summary>
    public const string Default = "scripted-1";

    /// <summary>"support" kod agent'inin modeli: once siparis durumu tool'unu cagirir, sonra yankilar.</summary>
    public const string Support = "scripted-support";

    /// <summary>"yonlendirici" kod agent'inin modeli: arka plan gorev tool'lariyla devreder, sonra yankilar.</summary>
    public const string Router = "scripted-router";

    /// <summary>"onay-agent" kod agent'inin modeli: onay isteyen bir tool cagirir, sonra sonucunu yankilar (Faz 55).</summary>
    public const string Approval = "scripted-approval";
}

/// <summary>
/// AgentPrism'i gercek bir Kestrel sunucusunda ayaga kaldirir.
/// </summary>
/// <remarks>
/// <para>
/// Faz 4'un <c>TestServer</c> deseni buraya tasinamaz: <c>TestServer</c> gercek
/// bir soket acmaz ve bir tarayici ona baglanamaz. Bu yuzden test kendi
/// <see cref="WebApplication"/> ornegini isletim sisteminin verdigi bir portta
/// baslatir.
/// </para>
/// <para>
/// Depolar bellek icidir ve model saglayicisi aga cikmaz; test hicbir dis
/// bagimlilik istemez.
/// </para>
/// </remarks>
internal sealed class UiHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly FakeModelProvider _provider;

    private UiHost(WebApplication app, FakeModelProvider provider, string baseAddress, string prefix)
    {
        _app = app;
        _provider = provider;
        BaseAddress = baseAddress;
        Prefix = prefix;
    }

    /// <summary>Sunucunun kok adresi. Ornek: <c>http://127.0.0.1:53412</c>.</summary>
    public string BaseAddress { get; }

    /// <summary>AgentPrism'in baglandigi yol oneki.</summary>
    public string Prefix { get; }

    /// <summary>Arayuzun adresi.</summary>
    public string UiAddress => BaseAddress + Prefix;

    /// <summary>Bir sunucu baslatir.</summary>
    /// <param name="prefix">Yol oneki.</param>
    /// <param name="authToken">Bearer token. Verilirse token katmani acilir.</param>
    /// <param name="configureServices">
    /// Ek servis kaydi (ornek: rol policy'lerini test etmek icin authentication/authorization).
    /// </param>
    /// <returns>Calisan sunucu.</returns>
    public static async Task<UiHost> StartAsync(
        string prefix = "/agentprism",
        string? authToken = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        configureServices?.Invoke(builder.Services);

        var provider = new FakeModelProvider(ScriptedModels.ProviderName)
            .ForModel(ScriptedModels.Default, cfg => cfg.EchoesUserMessage())
            .ForModel(ScriptedModels.Support, cfg => cfg
                .CallsTool("get_order_status", new { orderId = "ORD-7" })
                .EchoesUserMessage())
            .ForModel(ScriptedModels.Router, cfg => cfg
                .CallsTool(
                    "background_agents_start_task",
                    new { agentName = "support", input = "ORD-7 nerede", description = "siparis durumu arastirmasi" })
                .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
                .EchoesUserMessage())
            .ForModel(ScriptedModels.Approval, cfg => cfg
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult());

        // Ses uclarinin ihtiyaci yalnizca bu soyutlamalardir; AgentPrism.Voice
        // paketine referans YOKTUR. Tek ornek ikisine birden baglanir.
        builder.Services.AddSingleton<StubSpeechSynthesizer>();
        builder.Services.AddSingleton<ISpeechSynthesizer>(
            static provider => provider.GetRequiredService<StubSpeechSynthesizer>());
        builder.Services.AddSingleton<ISpeechTranscriber>(
            static provider => provider.GetRequiredService<StubSpeechSynthesizer>());

        builder.Services.AddAgentPrism()
            .AddModelProvider(provider)
            .AddToolsFrom(typeof(OrderTools))
            .UseUI()
            .UseWorkflows()

            // Konusma katmani (Faz 29). Cagrilmazsa WebSocket ucu hic acilmaz;
            // E2E testi bu yuzden acikca acar.
            .UseVoiceConversation()
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                DisplayName = "Support assistant",
                Description = "Testlerde kullanilan kod agent'i.",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModels.ProviderName,
                    Model = ScriptedModels.Support,
                },
                ToolNames = ["get_order_status"],
                Origin = AgentDefinitionOrigin.Code,
            })
            .AddAgent(new AgentDefinition
            {
                Name = "yonlendirici",
                DisplayName = "Router",
                Description = "Isi support agent'ina devreden kod agent'i.",
                Instructions = "Gerekirse support agent'ini cagir.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModels.ProviderName,
                    Model = ScriptedModels.Router,
                },
                CallableAgentNames = ["support"],
                Origin = AgentDefinitionOrigin.Code,
            })
            .AddAgent(new AgentDefinition
            {
                Name = "onay-agent",
                DisplayName = "Approval agent",
                Description = "Onay isteyen bir tool tasiyan, E2E testlerinde kullanilan kod agent'i (Faz 55).",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModels.ProviderName,
                    Model = ScriptedModels.Approval,
                },
                ToolNames = ["cancel_order"],
                Origin = AgentDefinitionOrigin.Code,
            })

            // Kodda tanimli iki workflow: biri duz bir zincir, digeri insan
            // girdisi bekleyen bir port. Arayuz testleri graf cizimini birinci,
            // bekleyen istek kartini ikinci uzerinden dogrular.
            .AddWorkflow(
                "ozetle-ve-cevir",
                static services => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential(
                    "ozetle-ve-cevir",
                    [
                        services.GetWorkflowAgent("ozetle-ve-cevir", "support"),
                        services.GetWorkflowAgent("ozetle-ve-cevir", "yonlendirici"),
                    ]),
                "Iki adimli zincir.")
            .AddWorkflow("onay-akisi", static _ => ApprovalWorkflow.Build(), "Insan onayi bekleyen akis.");

        var app = builder.Build();

        // Port 0: isletim sistemi bos bir port secer. Sabit bir port, paralel
        // kosan testlerde carpisir.
        app.Urls.Add("http://127.0.0.1:0");

        app.MapAgentPrism(prefix, options =>
        {
            if (authToken is { Length: > 0 })
            {
                options.AuthToken = authToken;
            }
        });

        await app.StartAsync();

        GuardUiAssets(app);

        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel bir adres bildirmedi.");

        return new UiHost(app, provider, address.TrimEnd('/'), '/' + prefix.Trim('/'));
    }

    /// <summary>
    /// Arayuz varliklari gercekten gomulu mu — degilse HEMEN ve ACIK bir
    /// mesajla dusur.
    /// </summary>
    /// <remarks>
    /// 🚨 Bu bir kolaylik degil, bir <strong>zaman asimi kalkani</strong>dir.
    /// <c>-p:AgentPrismFrontendEnabled=false</c> ile derlenen bir cozumde (hizli
    /// ic dongu) veya Node.js bulunmayan bir ortamda <c>AgentPrism.UI</c> hicbir
    /// varlik gommez. Bu kalkan olmadan her E2E testi bos bir sayfada
    /// "waiting for heading Dashboard" diyerek 30 saniye bekler; 41 testte bu
    /// <strong>~20 dakika</strong> eder ve hicbir hata mesaji gercek sebebi
    /// soylemez. Olculdu: Faz 41'de ayni belirti (o zaman sebep senkronizasyon
    /// kopyalariydi) 19 dakika kaybettirdi; Faz 47'de bu kez sebep eksik bir
    /// derleme bayragiydi. Kalkan iki sebebi de ayni anda ve saniyeler icinde
    /// gorunur kilar.
    /// </remarks>
    private static void GuardUiAssets(WebApplication app)
    {
        var provider = app.Services.GetService<IAgentPrismUiProvider>();

        if (provider is { HasAssets: true })
        {
            return;
        }

        throw new InvalidOperationException(
            "Arayuz varliklari gomulu degil; E2E testleri bos bir sayfaya bakardi ve her biri " +
            "30 saniye zaman asimina ugrardi. Cozumu ARAYUZ ACIK derleyin: " +
            "`dotnet build AgentPrism.slnx -c Release` (yani `-p:AgentPrismFrontendEnabled=false` " +
            "OLMADAN). Ayrica `find src -name \"* 2.*\" -not -path \"*/node_modules/*\"` bos " +
            "donmelidir: senkronizasyon kopyalari gomulu varlik listesini zehirler ve ayni " +
            "belirtiyi uretir.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();

        _provider.Dispose();
    }
}
