using AgentPrism.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// Model names in <see cref="UiHost"/>'s fake provider. Three separate models
/// carry three separate, independent response queues (<see cref="FakeModelProvider.ForModel"/>);
/// no state is shared between them.
/// </summary>
internal static class ScriptedModels
{
    /// <summary>Provider name.</summary>
    public const string ProviderName = "scripted";

    /// <summary>The default model used by ad-hoc agents created from the UI: it only echoes.</summary>
    public const string Default = "scripted-1";

    /// <summary>Model for the "support" code agent: calls the order status tool first, then echoes.</summary>
    public const string Support = "scripted-support";

    /// <summary>Model for the "yonlendirici" code agent: delegates via background task tools, then echoes.</summary>
    public const string Router = "scripted-router";

    /// <summary>Model for the "approval-agent" code agent: calls a tool that requests approval, then echoes its result (Phase 55).</summary>
    public const string Approval = "scripted-approval";
}

/// <summary>
/// Brings up AgentPrism on a real Kestrel server.
/// </summary>
/// <remarks>
/// <para>
/// Phase 4's <c>TestServer</c> pattern cannot be reused here: <c>TestServer</c>
/// does not open a real socket, so a browser cannot connect to it. The test
/// therefore starts its own <see cref="WebApplication"/> instance on a port
/// assigned by the operating system.
/// </para>
/// <para>
/// Stores are in-memory and the model provider never reaches the network; the
/// test has no external dependencies.
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

    /// <summary>The server's root address. Example: <c>http://127.0.0.1:53412</c>.</summary>
    public string BaseAddress { get; }

    /// <summary>The path prefix AgentPrism is bound to.</summary>
    public string Prefix { get; }

    /// <summary>The UI's address.</summary>
    public string UiAddress => BaseAddress + Prefix;

    /// <summary>Starts a server.</summary>
    /// <param name="prefix">Path prefix.</param>
    /// <param name="authToken">Bearer token. If given, the token layer is enabled.</param>
    /// <param name="configureServices">
    /// Extra service registration (e.g. authentication/authorization to test role policies).
    /// </param>
    /// <returns>The running server.</returns>
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
                    new { agentName = "support", input = "Where is ORD-7", description = "order status investigation" })
                .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
                .EchoesUserMessage())
            .ForModel(ScriptedModels.Approval, cfg => cfg
                .CallsTool("cancel_order", new { orderId = "ORD-7" })
                .EchoesLastToolResult());

        // Voice endpoints need only these abstractions; there is NO reference
        // to the AgentPrism.Voice package. A single instance backs both.
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

            // Voice conversation layer (Phase 29). If not called, the WebSocket
            // endpoint never opens; the E2E test therefore enables it explicitly.
            .UseVoiceConversation()
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                DisplayName = "Support assistant",
                Description = "Code agent used in tests.",
                Instructions = "Give a short answer.",
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
                Description = "Code agent that hands work off to the support agent.",
                Instructions = "Call the support agent if needed.",
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
                Name = "approval-agent",
                DisplayName = "Approval agent",
                Description = "Code agent used in E2E tests that carries a tool requiring approval (Phase 55).",
                Instructions = "Give a short answer.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModels.ProviderName,
                    Model = ScriptedModels.Approval,
                },
                ToolNames = ["cancel_order"],
                Origin = AgentDefinitionOrigin.Code,
            })

            // Two workflows defined in code: one is a plain chain, the other is
            // a port waiting on human input. The UI tests verify graph rendering
            // through the first and the pending-request card through the second.
            .AddWorkflow(
                "ozetle-ve-cevir",
                static services => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential(
                    "ozetle-ve-cevir",
                    [
                        services.GetWorkflowAgent("ozetle-ve-cevir", "support"),
                        services.GetWorkflowAgent("ozetle-ve-cevir", "yonlendirici"),
                    ]),
                "Two-step chain.")
            .AddWorkflow("approval-flow", static _ => ApprovalWorkflow.Build(), "Flow that waits for human approval.");

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
