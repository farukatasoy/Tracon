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

    /// <summary>Model for the "router" code agent: delegates via background task tools, then echoes.</summary>
    public const string Router = "scripted-router";

    /// <summary>Model for the "approval-agent" code agent: calls a tool that requests approval, then echoes its result (Phase 55).</summary>
    public const string Approval = "scripted-approval";

    /// <summary>Model for the "client-tool-agent" code agent: calls a client-side tool, then echoes its result (Phase 61).</summary>
    public const string ClientTool = "scripted-client-tool";

    /// <summary>Model for the "custom-event-agent" code agent: calls a tool that writes a RunEventType.Custom event, then echoes its result (Phase 141).</summary>
    public const string CustomEvent = "scripted-custom-event";
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
    /// <param name="configureApp">
    /// Wires up an additional route after <c>app.Build()</c> and before the
    /// <c>MapAgentPrism</c> call (example: a static test page hosting the
    /// embeddable widget's script tag, same origin as this host).
    /// </param>
    /// <returns>The running server.</returns>
    public static async Task<UiHost> StartAsync(
        string prefix = "/agentprism",
        string? authToken = null,
        Action<IServiceCollection>? configureServices = null,
        Action<WebApplication>? configureApp = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        configureServices?.Invoke(builder.Services);

        // Registered BEFORE AddAgentPrism() so TryAddSingleton does not overwrite it
        // (same pattern as IRunAttributionContext in samples/AgentPrism.Api).
        builder.Services.AddSingleton<IToolApprovalPresenter, ScriptedApprovalPresenter>();

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
                .EchoesLastToolResult())
            .ForModel(ScriptedModels.ClientTool, cfg => cfg
                .CallsTool("read_page_title")
                .EchoesLastToolResult("Title: "))
            .ForModel(ScriptedModels.CustomEvent, cfg => cfg
                .CallsTool("mark_preview_ready", new { orderId = "ORD-7" })
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
            .AddClientTool(
                "read_page_title",
                "Reads the current browser page title.",
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("""{"type":"object","properties":{}}"""))
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
                Name = "router",
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
            .AddAgent(new AgentDefinition
            {
                Name = "client-tool-agent",
                DisplayName = "Client tool agent",
                Description = "Code agent used in E2E tests that carries a client-side tool (Phase 61).",
                Instructions = "Give a short answer.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModels.ProviderName,
                    Model = ScriptedModels.ClientTool,
                },
                ToolNames = ["read_page_title"],
                Origin = AgentDefinitionOrigin.Code,
            })
            .AddAgent(new AgentDefinition
            {
                Name = "custom-event-agent",
                DisplayName = "Custom event agent",
                Description = "Code agent used in E2E tests that writes a RunEventType.Custom event (Phase 141).",
                Instructions = "Give a short answer.",
                Model = new ModelBinding
                {
                    Provider = ScriptedModels.ProviderName,
                    Model = ScriptedModels.CustomEvent,
                },
                ToolNames = ["mark_preview_ready"],
                Origin = AgentDefinitionOrigin.Code,
            })

            // Two workflows defined in code: one is a plain chain, the other is
            // a port waiting on human input. The UI tests verify graph rendering
            // through the first and the pending-request card through the second.
            .AddWorkflow(
                "summarize-and-translate",
                static services => Microsoft.Agents.AI.Workflows.AgentWorkflowBuilder.BuildSequential(
                    "summarize-and-translate",
                    [
                        services.GetWorkflowAgent("summarize-and-translate", "support"),
                        services.GetWorkflowAgent("summarize-and-translate", "router"),
                    ]),
                "Two-step chain.")
            .AddWorkflow("approval-flow", static _ => ApprovalWorkflow.Build(), "Flow that waits for human approval.");

        var app = builder.Build();

        // Port 0: the operating system picks a free port. A fixed port would
        // collide across tests running in parallel.
        app.Urls.Add("http://127.0.0.1:0");

        configureApp?.Invoke(app);

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
            ?? throw new InvalidOperationException("Kestrel did not report an address.");

        return new UiHost(app, provider, address.TrimEnd('/'), '/' + prefix.Trim('/'));
    }

    /// <summary>
    /// Checks whether the UI assets are actually embedded — if not, fail
    /// IMMEDIATELY with a CLEAR message.
    /// </summary>
    /// <remarks>
    /// 🚨 This is not a convenience, it is a <strong>timeout shield</strong>.
    /// In a solution built with <c>-p:AgentPrismFrontendEnabled=false</c> (fast
    /// inner loop) or in an environment without Node.js, <c>AgentPrism.UI</c>
    /// embeds no assets at all. Without this shield, every E2E test waits 30
    /// seconds on a blank page saying "waiting for heading Dashboard"; across
    /// 41 tests that adds up to <strong>~20 minutes</strong>, and no error
    /// message states the real cause. Measured: in Phase 41 the same symptom
    /// (caused that time by synchronization copies) wasted 19 minutes; in
    /// Phase 47 the cause was a missing build flag instead. The shield makes
    /// both causes visible at once and within seconds.
    /// </remarks>
    private static void GuardUiAssets(WebApplication app)
    {
        var provider = app.Services.GetService<IAgentPrismUiProvider>();

        if (provider is { HasAssets: true })
        {
            return;
        }

        throw new InvalidOperationException(
            "UI assets are not embedded; E2E tests would stare at a blank page and each " +
            "would hit a 30-second timeout. The fix is to build WITH THE UI ENABLED: " +
            "`dotnet build AgentPrism.slnx -c Release` (that is, WITHOUT " +
            "`-p:AgentPrismFrontendEnabled=false`). Also, `find src -name \"* 2.*\" -not -path \"*/node_modules/*\"` " +
            "must return empty: synchronization copies poison the embedded asset list and " +
            "produce the same symptom.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();

        _provider.Dispose();
    }
}
