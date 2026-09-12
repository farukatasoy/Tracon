// Tracon sample host.
//
// This project demonstrates Tracon's capabilities in a real ASP.NET Core
// application: tool registration, code-defined agents, the catalog, run
// recording, database persistence, and multiple model providers.
//
// Two configurations are both supported (the "zero surprises" rule):
//   - an API key is configured  -> real OpenAI models
//   - no API key is configured  -> EchoModelProvider, which makes no network calls
//   - a connection string is configured -> a SQL persistence store; otherwise
//     in-memory stores
//
// HTTP layer: a single `app.MapTracon("/tracon")` call wires up the
// management API and the OpenAI-compatible run endpoints.
//
// UI: the `.UseUI()` call registers the UI assets and opens a control plane
// running at http://localhost:5080/tracon.
//
// Observability, tool approval, and remote MCP tools:
//   - spans and metrics are produced automatically (TraconDiagnostics)
//   - the `cancel_order` tool requires approval; the UI shows an approval card
//   - `.UseMcp()` discovers tools from remote MCP servers
//
// Provider expansion and health checks:
//   - `.UseOpenAICompatible(name, ...)` connects to any OpenAI-compatible
//     endpoint (OpenRouter, Groq, vLLM, local Ollama/LM Studio...)
//   - `/tracon/api/models/health` checks provider reachability (produces
//     no cost); a circuit breaker temporarily stops a provider after
//     consecutive failures
//
// Agent-calls-agent:
//   - `CallableAgentNames` grants an agent permission to call other agents
//   - each sub-call produces a SEPARATE `runs` row; the UI draws it as a tree
//   - depth, token, and count limits are configured under `Tracon:AgentGraph`
//
// Workflow execution:
//   - `.UseWorkflows()` turns on the engine; `AddWorkflow(...)` defines a free-form
//     graph in code
//   - a workflow defined from the UI can only chain agents already in the
//     catalog (decision K2)
//   - each execution is one `runs` row; the agents inside it attach beneath it
//   - a checkpoint is written at every super-step, so unfinished work can resume
//
// First-class Anthropic and Google providers:
//   - `.UseAnthropic(...)` and `.UseGoogle(...)` — both use the OFFICIAL SDK
//   - `ModelBinding.ProviderSettings` carries provider-specific, per-agent
//     settings (prompt caching, thinking budget, Gemini safety thresholds)
//   - a response that comes back EMPTY due to a safety filter is recorded as a
//     `content_filtered` error
//
// Azure OpenAI:
//   - `.UseAzureOpenAI(...)` — an ENDPOINT is required; Azure has no single
//     global address
//   - 🚨 `ModelBinding.Model` carries the DEPLOYMENT name, not the model name,
//     for this provider
//   - credentials are supplied via API key or Microsoft Entra; `Azure.Identity`
//     is NOT a dependency of Tracon, the credential factory comes from the
//     consumer
//
// See docs/arsiv/fazlar/04-HTTP-API.md, docs/arsiv/fazlar/05-TRACON-UI.md, docs/arsiv/fazlar/06-GOZLEMLENEBILIRLIK.md,
//     docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md, docs/arsiv/fazlar/12-AGENT-CAGRI-GRAFIGI.md,
//     docs/arsiv/fazlar/15-WORKFLOWS-YURUTME.md, docs/arsiv/fazlar/26-ANTHROPIC-VE-GEMINI.md,
//     docs/arsiv/fazlar/27-AZURE-FOUNDRY.md
//
// Set secrets before running:
//   dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" "Host=localhost;Database=Tracon;Username=...;Password=..."
//
// For SQL Server (instead of PostgreSQL; not given at the same time as either):
//   dotnet user-secrets set "Tracon:SqlServer:ConnectionString" "Server=localhost,1433;Database=Tracon;User Id=sa;Password=...;TrustServerCertificate=true"
//   dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "sk-..."
//   dotnet user-secrets set "Tracon:Providers:OpenAICompatible:openrouter:ApiKey" "sk-or-..."
//   dotnet user-secrets set "Tracon:Providers:Anthropic:ApiKey" "sk-ant-..."
//   dotnet user-secrets set "Tracon:Providers:Google:ApiKey" "AIza..."
//   dotnet user-secrets set "Tracon:Providers:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
//   dotnet user-secrets set "Tracon:Providers:AzureOpenAI:ApiKey" "..."

using System.Text.Json;
using Tracon;
using Tracon.Api;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Quality;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// Role-based authorization (phase 9).
//
// 🚨 Tracon defines only the policy NAMES (TraconPolicies.Reader /
// Operator / Admin); the consumer binds them to its own identity system. When a
// name is not registered, every RequireRole(...) call inside Tracon is a
// NO-OP and the endpoint keeps only the three-layer guard (loopback, bearer
// token, general policy). That is the documented upgrade-safe behavior — but it
// also meant this reference application could never SHOW a role blocking
// anything.
//
// Turning `Tracon:Demo:Roles:Enabled` on registers the three names and a
// demonstration identity scheme that reads the role from a request header. The
// scheme verifies NOTHING and is unfit for production; a real deployment binds
// the same three names to OpenID Connect, JWT bearer or Windows authentication.
var demoRolesEnabled = builder.Configuration.GetValue<bool>("Tracon:Demo:Roles:Enabled");

if (demoRolesEnabled)
{
    builder.Services
        .AddAuthentication(DemoRoleAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DemoRoleAuthenticationHandler>(
            DemoRoleAuthenticationHandler.SchemeName,
            configureOptions: null);

    // Reader ⊂ Operator ⊂ Admin: a higher role satisfies the lower policy too.
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(
            TraconPolicies.Reader,
            policy => policy.RequireRole(DemoRoles.Reader, DemoRoles.Operator, DemoRoles.Admin))
        .AddPolicy(
            TraconPolicies.Operator,
            policy => policy.RequireRole(DemoRoles.Operator, DemoRoles.Admin))
        .AddPolicy(
            TraconPolicies.Admin,
            policy => policy.RequireRole(DemoRoles.Admin));
}

// The OpenAPI document is the CONSUMER's choice. Tracon.AspNetCore does
// NOT depend on this package; endpoints carry metadata from the shared
// framework and appear in the document automatically once AddOpenApi() is
// called.
builder.Services.AddOpenApi();

// Run attribution (phase 68): who ran this, and for which job. Registered BEFORE
// AddTracon() so it wins the TryAdd — the consumer's binding to its own
// identity pipeline always beats the built-in default.
//
// 🚨 The user is NEVER read from the run request body; see the class remarks.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IRunAttributionContext, DemoRunAttributionContext>();

// Tool-approval presentation (phase 142): turns the cancel_order approval
// card's raw `{ "orderId": "ORD-1001" }` into "Cancel order for Priya Shah".
// Registered BEFORE AddTracon() for the same reason as
// IRunAttributionContext above — TryAddSingleton must not overwrite it.
builder.Services.AddSingleton<IToolApprovalPresenter, OrderApprovalPresenter>();

// Response caching (Phase 81, F-45) needs an IDistributedCache; Tracon
// never registers one itself (an agent that enables ResponseCache without
// this line fails to compile with a message naming exactly this gap).
// AddDistributedMemoryCache() is enough for a single-instance sample; a real
// multi-instance deployment points this at Redis/SQL Server instead.
builder.Services.AddDistributedMemoryCache();

var tracon = builder.AddTracon()
    // Tools are defined ONLY in code. The UI lets a user pick from this list;
    // it never lets them write tool code. This is a security boundary.
    // Methods not marked with [TraconTool] are never scanned.
    // Registered by a compile-time source generator — NO REFLECTION, no AOT
    // warning. It finds every [TraconTool]-marked method in this assembly
    // (OrderTools). Use AddToolsFrom for tools defined in another assembly.
    .AddGeneratedTools()
    // A client-side tool (Phase 61): its DECLARATION lives here, in code, like
    // every other tool (K2), but the server never runs it. The model's call
    // is returned to the caller (a browser), which reads its own shopping
    // cart from local storage — something only the browser can see — and
    // sends the result back via `toolResults` on the next run request.
    .AddClientTool(
        "read_shopping_cart",
        "Reads the items currently in the customer's shopping cart in the browser.",
        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type":"object","properties":{}}"""))
    // Remote MCP servers. A server definition is added from the UI or the
    // /api/mcp-servers endpoint; discovery runs in the background. Nothing
    // happens if no server is registered. MCP tools require approval by
    // default.
    // Settings are read from the `Tracon:Mcp` section; defaults apply if
    // the section is absent. 🚨 Without this overload, an environment
    // variable such as `Tracon:Mcp:RefreshInterval` did NOTHING, WITH NO
    // ERROR AT ALL (measured; decision K-353).
    .UseMcp(builder.Configuration.GetSection(TraconMcpOptions.SectionName), configure: null)
    // The other side of the mirror — exposes Tracon's own agents to the
    // outside. "summarizer" is chosen deliberately: it carries no tool, so it
    // never touches the approval boundary. Default MaxDepth=1: an inbound
    // call can delegate at most one level further (the same depth check as
    // ChildAgentInvoker). EnableTasks is off by default; the sample turns it
    // on to demonstrate the MCP Tasks extension end to end.
    .UseMcpServer(o =>
    {
        o.ExposedAgents.Add("summarizer");
        o.EnableTasks = true;
    })
    .UseA2A(o => o.ExposedAgents.Add("summarizer"))
    // The workflow execution engine. Agents in the catalog are wired together
    // with ready-made patterns. If the engine is not registered, definitions
    // are still manageable — only the run endpoint returns 501.
    .UseWorkflows()
    // The embedded management UI. No separate mapping call is needed:
    // MapTracon finds the registration and mounts the UI under the same
    // prefix.
    .UseUI()
    // Content moderation. 🚨 WITHOUT this call, no prompt is filtered, no
    // response is inspected, and no ring is added to the model pipeline: the
    // registration is the gate for K1 (zero surprises), not an Enabled flag.
    //
    // The sample app DELIBERATELY turns the guard on, so that this capability
    // is demonstrable. The built-in guard makes two decisions: a denied term
    // -> Block, a PII pattern -> Mask. The card-number pattern runs Luhn
    // validation, so an order number is never masked by mistake.
    //
    // A consumer plugs in their own rule set by implementing IContentGuard
    // and calling .AddContentGuard<T>(); multiple guards run in sequence and
    // the strictest verdict wins.
    .AddPatternContentGuard(options =>
    {
        options.MaskedPii = PiiPatterns.CreditCard | PiiPatterns.Email | PiiPatterns.ProviderApiKey;
        options.DeniedTerms.Add("confidential-project");
    })
    // At-rest content protection. 🚨 WITHOUT this call, the settings below
    // are read but never applied: the registration itself is K1's gate, same
    // as the content guard above. Settings (Enabled/ActiveKeyId/Keys) live in
    // appsettings.json; the raw key material never does (see the "//" note
    // next to ContentProtection there) — only when a SQL provider is also
    // configured does a missing key value surface, and only then (session
    // state, run input, tool arguments/results, agent files, and attachments
    // are the columns this sample leaves in scope).
    .AddContentProtection();

// The provider is optional. Without an API key, the app runs with a sample
// provider that makes no network calls; nothing breaks.
var openAi = builder.Configuration.GetSection(OpenAIProviderOptions.SectionName);
var openAiEnabled = !string.IsNullOrWhiteSpace(openAi["ApiKey"]);

if (openAiEnabled)
{
    // This single call registers two providers: "openai" (Chat Completions)
    // and "openai-responses" (Responses API). An agent definition chooses
    // between them through ModelBinding.Provider.
    tracon.UseOpenAI(openAi);

    // Provider-hosted live voice. ⚠️ CHANGES THE HOSTING MODEL and opens an
    // outbound connection billed BY THE SECOND, which is why it is a separate
    // call rather than a flag on UseOpenAI: an application opts into the spend
    // explicitly.
    //
    // The provider runs the conversation and carries the audio straight to the
    // browser over WebRTC. Tracon stands in two places: it creates the
    // session — so tenancy, role and concurrency gates apply and the raw API key
    // never reaches the browser — and it attaches a server-side control
    // connection that turns the work the model delegates into ORDINARY runs,
    // with the same tools, guards, quota, cost and audit trail as
    // POST /api/agents/{name}/run.
    //
    // This is independent of UseVoiceConversation() above: that layer has
    // Tracon transcribe, run and synthesize for providers with no realtime
    // API. Both can be on at once.
    tracon.UseOpenAILive(
        builder.Configuration.GetSection(OpenAILiveOptions.SectionName));

    tracon.UseLiveVoice(
        builder.Configuration.GetSection(VoiceLiveOptions.SectionName));

    // Image generation is separately enabled by Tracon:Images. Registering
    // this keyed adapter alone does not expose generate_image or map its endpoint.
    tracon.UseOpenAIImages();

    // Knowledge base / semantic search. Tracon does NOT choose an
    // embedding model (the pattern behind K-032: model names change faster
    // than NuGet release cadence); the consumer registers their own provider.
    // The dimension (TraconKnowledgeOptions.Dimensions, default 1536)
    // matches "text-embedding-3-small".
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        new OpenAIClient(openAi["ApiKey"])
            .GetEmbeddingClient("text-embedding-3-small")
            .AsIEmbeddingGenerator());
}
else
{
    tracon.AddModelProvider(new EchoModelProvider());
}

// Any OpenAI-COMPATIBLE endpoint. Same configuration shape, different
// sub-section: Tracon:Providers:OpenAICompatible:openrouter:*. Without an
// ApiKey the provider is never registered — the "zero surprises" rule applies
// here too.
var openRouter = builder.Configuration.GetSection($"{OpenAICompatibleProviderOptions.SectionName}:openrouter");
var openRouterEnabled = !string.IsNullOrWhiteSpace(openRouter["ApiKey"]);

if (openRouterEnabled)
{
    tracon.UseOpenAICompatible("openrouter", openRouter);
}

// Anthropic (Claude) — a first-class provider. The official `Anthropic` SDK
// carries its own IChatClient adapter, so the shape matches Tracon.OpenAI.
// Without a key, the provider is never registered.
var anthropic = builder.Configuration.GetSection(AnthropicProviderOptions.SectionName);
var anthropicEnabled = !string.IsNullOrWhiteSpace(anthropic["ApiKey"]);

if (anthropicEnabled)
{
    tracon.UseAnthropic(anthropic);
}

// Google Gemini — a first-class provider. The provider name is "google", not
// "gemini": the same package may cover Vertex AI in the future.
var google = builder.Configuration.GetSection(GoogleProviderOptions.SectionName);
var googleEnabled = !string.IsNullOrWhiteSpace(google["ApiKey"]);

if (googleEnabled)
{
    tracon.UseGoogle(google);
    tracon.UseGoogleImages();
}

// Azure OpenAI — the default path in the enterprise .NET world. Opening the
// provider requires an ENDPOINT; Azure has no single global address.
//
// 🚨 On this provider, ModelBinding.Model carries the DEPLOYMENT name, not
// the model name.
//
// This sample has NO reference to `Azure.Identity`; the API-key path is
// shown instead. Managed identity is enabled by chaining a second call:
//
//   tracon.UseAzureOpenAI(azureOpenAI)
//             .UseAzureOpenAI(o => o.CredentialFactory =
//                 static () => new DefaultAzureCredential());
var azureOpenAI = builder.Configuration.GetSection(AzureOpenAIProviderOptions.SectionName);
var azureOpenAIEnabled = !string.IsNullOrWhiteSpace(azureOpenAI["Endpoint"])
    && !string.IsNullOrWhiteSpace(azureOpenAI["ApiKey"]);

if (azureOpenAIEnabled)
{
    tracon.UseAzureOpenAI(azureOpenAI);
    tracon.UseAzureOpenAIImages();
}

// Voice tools. Without a key, no tool is registered and the /api/voice/*
// endpoints return 501 — the app still runs.
//
// The generated audio is written to the `attachments` table and the tool
// returns only the attachment's ID to the model. Putting the raw audio in
// the tool result would fill the context window with base64.
var voice = builder.Configuration.GetSection(VoiceOptions.SectionName);
var voiceEnabled = !string.IsNullOrWhiteSpace(voice["ApiKey"]);

if (voiceEnabled)
{
    tracon.UseVoice(voice);

    // Real-time speech. ⚠️ CHANGES THE HOSTING MODEL:
    // /tracon/api/voice/sessions/{id}/stream opens a WebSocket and the
    // connection lives for minutes. The connection is bound to ONE server
    // instance; a multi-instance deployment needs a sticky session, and the
    // reverse proxy must allow the WebSocket upgrade.
    //
    // If this call is not made, no WebSocket endpoint opens and behavior does
    // not change. Conversation needs BOTH resolution and synthesis; both come
    // with UseVoice.
    tracon.UseVoiceConversation(
        builder.Configuration.GetSection(VoiceConversationOptions.SectionName));
}

// A local model server example (Ollama/LM Studio). Setup is the SAME call as
// any OpenAI-compatible endpoint; the only difference is omitting ApiKey (the
// local server does not require one) and pointing at the local address. This
// example is OFF by default: Ollama may not be running on most developer
// machines, and a closed port only drops that provider from the list without
// breaking anything else. To try it:
//
//   ollama serve
//   ollama pull llama3.1
//
// then enable the two lines below:
//
// tracon.UseOpenAICompatible("ollama", o =>
// {
//     o.Endpoint = new Uri("http://localhost:11434/v1");
//     o.DefaultModel = "llama3.1";
//     // No ApiKey — the local server does not require one. OpenAIClient does
//     // not accept an empty credential, so Tracon uses a fixed
//     // placeholder (unrelated to OPENAI001; the provider never sees it).
// });
//
// A known difference: Ollama's tool_choice support varies by model; on
// servers that do not send usage in the stream, RunRecord.TotalTokens stays
// null — this is not a bug (see docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md, section
// 8.2).

var model = openAiEnabled
    ? new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = openAi["DefaultModel"] ?? "gpt-5.4-mini",
    }
    : new ModelBinding { Provider = "echo", Model = "echo-1" };

// Online evaluation. 🚨 This call only registers WHICH model is USED as the
// judge — by itself it SCORES nothing. The real gate is
// `Tracon:OnlineEvaluation:Enabled` AND `:SampleRate` (K1); both are OFF
// in the committed appsettings and are only turned on at runtime through an
// environment variable (see docs/arsiv/fazlar/49-CEVRIMICI-DEGERLENDIRME.md, DoD).
if (openAiEnabled)
{
    tracon.AddModelRunJudge(options =>
    {
        // In a real setup, a cheap model SEPARATE from the one being measured
        // is chosen (the same model scoring its own output is biased). This
        // sample uses the same value because only one model is registered.
        options.Model = model;
        options.Criteria.Add("Does the answer address the question directly and correctly?");
        options.Criteria.Add("Was the right tool called for order questions?");
    });

    // A calibrated evaluator from Microsoft.Extensions.AI.Evaluation.Quality,
    // bound as a second judge. Tracon does not reference that package; this
    // sample adds it itself, which is what a consumer does. Every metric the
    // evaluator reports becomes its own score row named `relevance.{metric}`,
    // and the judge-name prefix is what keeps two evaluators reporting the same
    // metric from overwriting each other.
    //
    // 🚨 Each evaluator costs a model call on every sampled run.
    tracon.AddEvaluatorJudge("relevance", new RelevanceEvaluator());
}

tracon
    // A declarative agent in code. Appears in the catalog with the "code"
    // source and takes precedence over a database definition with the same name.
    .AddAgent(new AgentDefinition
    {
        Name = "support",
        DisplayName = "Support Assistant",
        Description = "Answers order and shipping questions.",
        Instructions = "You are a support assistant. Answer briefly and clearly. " +
                       "Always use a tool for order questions. Use read_shopping_cart " +
                       "when asked about the customer's current cart. Use " +
                       "estimate_shipping_cost when asked for a shipping estimate.",
        Model = model,
        // cancel_order requires approval: when the model tries to call it, the
        // run pauses and an approval card appears in the UI. read_shopping_cart
        // is a client-side tool (Phase 61): the server never runs it, and the
        // model's call comes back to the caller as a pending FunctionCallContent.
        // estimate_shipping_cost (F-176, 135.1) takes an OBJECT parameter -
        // the model sends a nested JSON argument, not a scalar.
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order", "read_shopping_cart", "estimate_shipping_cost", "mark_preview_ready"],
    })

    // A harness-configured agent: context compaction and todo tracking are on.
    // File access is deliberately absent from HarnessSettings; in MAF it only
    // activates when a value is assigned, and Tracon never assigns one (K-062).
    .AddAgent(new AgentDefinition
    {
        Name = "researcher",
        DisplayName = "Researcher",
        Description = "Reviews order records and summarizes findings.",
        Instructions = "You are a researcher. Proceed step by step and summarize your findings.",
        Model = model,
        ToolNames = ["get_order_status"],
        Harness = new HarnessSettings
        {
            MaxContextWindowTokens = 32_000,
            MaximumIterationsPerRequest = 8,
            DisableWebSearch = true,
            DisableFileMemory = true,
        },
    })

    // Agent-calls-agent. The router itself does not use a tool; it hands the
    // work off to the specialist agent. Each hand-off produces a SEPARATE
    // `runs` row and appears as a tree in the UI's run detail view.
    //
    // Limits come from the Tracon:AgentGraph section (default: depth 3,
    // 200,000 tokens per tree, 25 sub-runs) and are shared across the whole
    // tree through a SINGLE budget object.
    //
    // "support" was chosen DELIBERATELY as the sub-agent, not "researcher":
    // the researcher uses a harness, and the harness defect documented in
    // K-053 (tool calls do not connect) would also hit the sub-run. Working
    // samples come before samples that explain the architecture.
    .AddAgent(new AgentDefinition
    {
        Name = "router",
        DisplayName = "Router",
        Description = "Hands off an incoming request to the right specialist agent.",
        Instructions = "You are a router. Hand off order questions to the 'support' agent, " +
                       "wait for its result, and summarize it for the user. Do not call tools yourself.",
        Model = model,
        CallableAgentNames = ["support"],
    })

    // Two links for the Sequential chain. Neither uses a tool: the proof of
    // the chain is that each link sees the OUTPUT of the previous one, and a
    // tool call would blur that proof.
    .AddAgent(new AgentDefinition
    {
        Name = "summarizer",
        DisplayName = "Summarizer",
        Description = "Summarizes incoming text in three bullet points.",
        Instructions = "Summarize the incoming text in at most three short bullet points. Do not add commentary.",
        Model = model,
    })
    .AddAgent(new AgentDefinition
    {
        Name = "translator",
        DisplayName = "Translator",
        Description = "Translates incoming text into English.",
        Instructions = "Translate the incoming text into English. Return only the translated text.",
        Model = model,
    })

    // Structured response validation (Phase 131). Tracon:StructuredResponse:Enabled
    // is on below, so a response that is not a valid JSON document fails the run
    // with RunErrorClass.StructuredResponseInvalid instead of closing successfully.
    .AddAgent(new AgentDefinition
    {
        Name = "order-summary",
        DisplayName = "Order Summary (structured output demo)",
        Description = "Summarizes an order as a JSON document.",
        Instructions = "Reply with ONLY a JSON object of the shape " +
                       "{\"orderId\": string, \"summary\": string}. No prose, no markdown fences.",
        Model = model with
        {
            ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.Json },
        },
        ToolNames = ["get_order_status"],
    })

    // Response caching and concurrent tool calls (Phase 81, F-45/F-134). The
    // SECOND identical run never reaches the model (usage is zero, no 'chat'
    // span) but the cached FunctionCallContent still runs get_order_status
    // again - a cache hit is not a shortcut around the tool loop.
    .AddAgent(new AgentDefinition
    {
        Name = "cached-support",
        DisplayName = "Cached Support (demo)",
        Description = "Same as 'support', with response caching and concurrent tool calls turned on.",
        Instructions = "You are a support assistant. Answer briefly and clearly. " +
                       "Always use a tool for order questions.",
        Model = model with
        {
            ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(10) },
            AllowConcurrentToolCalls = true,
        },
        ToolNames = ["get_order_status", "list_recent_orders"],
    });

// A workflow defined in CODE. A free-form graph can only be built here -
// the UI only defines ready-made patterns (design rule K2). This sample
// uses the ready-made pattern factory; attaching custom `Executor` types
// with `WorkflowBuilder` is also possible.
tracon.AddWorkflow(
    "summarize-and-translate",
    static services => AgentWorkflowBuilder.BuildSequential(
        "summarize-and-translate",
        [
            // 🚨 Agents are attached with `GetWorkflowAgent`, never taken
            // DIRECTLY from the catalog. An agent taken directly opens its own
            // root `runs` row and the workflow tree appears empty. Measured:
            // in the sample app the tree returned one row instead of three
            // (see docs/arsiv/fazlar/15-WORKFLOWS-YURUTME.md).
            services.GetWorkflowAgent("summarize-and-translate", "summarizer", "Summarizes incoming text in three bullet points."),
            services.GetWorkflowAgent("summarize-and-translate", "translator", "Translates incoming text into English."),
        ]),
    "Summarizes text, then translates it into English. Defined in code.");

// A workflow that waits for human input. When the graph reaches an EXTERNAL
// REQUEST PORT, execution pauses, the state is written to a checkpoint, and
// the run closes as `AwaitingInput`. The response is given through
// `POST /api/workflows/runs/{runId}/respond` and opens a NEW run - the event
// stream is append-only (K-014).
tracon.AddWorkflow(
    "summarize-and-approve",
    static services =>
    {
        var port = RequestPort.Create<string, bool>("publish-approval");

        var summarize = services.GetWorkflowAgent(
            "summarize-and-approve",
            "summarizer",
            "Summarizes incoming text in three bullet points.");

        // 🚨 A TRANSLATOR sits between the agent and the port. The agent host
        // emits `List<ChatMessage>`, but the port expects `string`; if the two
        // are connected directly, the port is called but CANNOT PROCESS the
        // message and produces no request - the run silently "completes"
        // without producing output. Measured: the port was called three
        // times with zero RequestInfoEvent.
        var ask = ExecutorBindingExtensions.BindAsExecutor(
            static (List<ChatMessage> messages) =>
                "Should this summary be published?" + Environment.NewLine + Environment.NewLine +
                (messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                 ?? string.Empty),
            id: "approval-question");

        // 🚨 The output type is declared from the HANDLER'S RETURN TYPE. A
        // handler whose body calls YieldOutputAsync but has no return value
        // declares no output type, and at runtime it fails with
        // "Cannot output object of type ... Expecting one of []" (measured).
        var publish = ExecutorBindingExtensions.BindAsExecutor(
            static (bool approved) => approved
                ? "Summary published."
                : "Publication canceled; summary kept in the archive.",
            id: "publish");

        // Bindings are set up ONCE and reused: each call would otherwise
        // produce a new object, and the edges would appear connected to two
        // separate nodes instead of the same one.
        // 🚨 `ForwardIncomingMessages` is turned off. When on, the agent host
        // forwards both the incoming message and its own response downstream;
        // the next node then runs TWICE and produces two separate pending
        // requests instead of one approval. Measured: in a real run,
        // `/requests` returned two records.
        var summarizeBinding = new AIAgentBinding(
            summarize,
            new AIAgentHostOptions
            {
                EmitAgentResponseEvents = true,
                EmitAgentUpdateEvents = true,
                ForwardIncomingMessages = false,
            });
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        return new WorkflowBuilder(summarizeBinding)
            .AddEdge(summarizeBinding, ask)
            .AddEdge(ask, portBinding)
            .AddEdge(portBinding, publish)
            .WithOutputFrom(publish)
            .WithName("summarize-and-approve")
            .Build();
    },
    "Summarizes text, then waits for human approval to publish. Defined in code.");

// A code function usable as a workflow node (phase 71). AddWorkflowFunction
// registers the function by NAME; a Sequential definition's `nodes` list can
// then mix it in with catalog agents (K2: only the name crosses into the
// definition, the body always lives in code - the same shape
// AgentDefinition.ToolNames already uses for tools). This particular node
// makes NO model call - it demonstrates the phase's own motivation: a real
// pipeline has steps (formatting, counting, file I/O) that are not AI calls
// and previously could not enter a workflow graph at all.
//
// A workflow whose "nodes" field uses this function is created through
// PUT /tracon/api/workflows/{name} - AddWorkflow() is for a free-form
// graph defined ENTIRELY in code; a "nodes" list is a stored definition like
// any agent-only Sequential workflow, just with a function mixed in. 🚨 The
// console's workflow editor does NOT offer a function picker yet (phase 71
// shipped the read side only - the graph draws a function node distinctly
// from an agent node); building one from the UI still requires this HTTP
// call directly, or a future phase's editor work.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
    "word-count",
    static _ => (messages, _, _) =>
    {
        var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                   ?? string.Empty;
        var count = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return new ValueTask<List<ChatMessage>>([new ChatMessage(ChatRole.User, $"{text}\n\n(word count: {count})")]);
    },
    "Appends a word count to the incoming text. Runs no model call.");

if (openRouterEnabled)
{
    // Same support scenario, different provider. Proof of provider expansion:
    // the agent definition points to a completely different (non-official
    // OpenAI) endpoint just by changing ModelBinding.Provider.
    tracon.AddAgent(new AgentDefinition
    {
        Name = "openrouter-support",
        DisplayName = "OpenRouter Support",
        Description = "Same support scenario, runs through OpenRouter.",
        Instructions = "You are a support assistant. Answer briefly and clearly. " +
                       "Always use a tool for order questions.",
        Model = new ModelBinding
        {
            Provider = "openrouter",
            // OpenRouter model identifiers carry a provider prefix; not
            // "gpt-5.4-mini" but "openai/gpt-5.4-mini". Measured: docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md.
            Model = openRouter["DefaultModel"] ?? "openai/gpt-5.4-mini",
            // OpenRouter's credit check treats max_tokens as a "worst case";
            // the default (65536) produces HTTP 402 on low-balance keys.
            // Measured: docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md.
            MaxOutputTokens = 512,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });
}

if (anthropicEnabled)
{
    // Same support scenario, on Claude. The tool-call mapping difference
    // (tool_use / tool_result blocks) is verified here.
    tracon.AddAgent(new AgentDefinition
    {
        Name = "claude-support",
        DisplayName = "Claude Support",
        Description = "Same support scenario, runs through Anthropic Claude.",
        Instructions = "You are a support assistant. Answer briefly and clearly. " +
                       "Always use a tool for order questions.",
        Model = new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = anthropic["DefaultModel"] ?? "claude-haiku-4-5-20251001",

            // 🚨 max_tokens is REQUIRED in the Anthropic Messages API. If left
            // empty, AnthropicProviderOptions.DefaultMaxOutputTokens is used.
            MaxOutputTokens = 1024,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });

    // An end-to-end proof of provider-specific settings (ProviderSettings):
    // extended thinking turned on. With thinking on, Anthropic only allows a
    // temperature of 1, so Temperature is not set.
    tracon.AddAgent(new AgentDefinition
    {
        Name = "claude-thinking",
        DisplayName = "Claude Thinking",
        Description = "Extended thinking turned on; configured through ProviderSettings.",
        Instructions = "Think step by step, then give a short conclusion.",
        Model = new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = anthropic["DefaultModel"] ?? "claude-haiku-4-5-20251001",
            MaxOutputTokens = 4096,
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [AnthropicProviderNames.ThinkingBudgetTokensSetting] = JsonSerializer.SerializeToElement(2048),
            },
        },
    });
}

if (googleEnabled)
{
    // Same support scenario, on Gemini. The tool-call mapping difference
    // (functionCall / functionResponse parts) is verified here.
    tracon.AddAgent(new AgentDefinition
    {
        Name = "gemini-support",
        DisplayName = "Gemini Support",
        Description = "Same support scenario, runs through Google Gemini.",
        Instructions = "You are a support assistant. Answer briefly and clearly. " +
                       "Always use a tool for order questions.",
        Model = new ModelBinding
        {
            Provider = GoogleProviderNames.Google,
            Model = google["DefaultModel"] ?? "gemini-3.6-flash",
            MaxOutputTokens = 2048,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });

    // Safety thresholds set to the STRICTEST level. This agent demonstrates
    // that a response filtered down to empty is recorded as a
    // "content_filtered" error.
    tracon.AddAgent(new AgentDefinition
    {
        Name = "gemini-strict-filter",
        DisplayName = "Gemini Strict Filter",
        Description = "All safety thresholds set to the strictest level; demonstrates filter behavior.",
        Instructions = "Answer the user's request.",
        Model = new ModelBinding
        {
            Provider = GoogleProviderNames.Google,
            Model = google["DefaultModel"] ?? "gemini-3.6-flash",
            MaxOutputTokens = 1024,
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [GoogleProviderNames.SafetyHarassmentSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
                [GoogleProviderNames.SafetyHateSpeechSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
                [GoogleProviderNames.SafetyDangerousContentSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
                [GoogleProviderNames.SafetySexuallyExplicitSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
            },
        },
    });
}

if (azureOpenAIEnabled)
{
    // Same support scenario, on Azure OpenAI.
    //
    // 🚨 The model field carries the DEPLOYMENT name. The value below must
    // match the deployment name defined in your Azure resource; if a model
    // name (e.g. "gpt-5.4-mini") is used instead, the request returns HTTP 404.
    tracon.AddAgent(new AgentDefinition
    {
        Name = "azure-support",
        DisplayName = "Azure Support",
        Description = "Same support scenario, runs through an Azure OpenAI deployment.",
        Instructions = "You are a support assistant. Answer briefly and clearly. " +
                       "Always use a tool for order questions.",
        Model = new ModelBinding
        {
            Provider = AzureOpenAIProviderNames.AzureOpenAI,
            Model = azureOpenAI["DefaultDeployment"] ?? "production-gpt",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });
}

// Voice assistant. The agent is only defined if voice tools are REGISTERED:
// a definition pointing to a nonexistent tool does not compile, and the
// application fails at startup.
if (voiceEnabled && openAiEnabled)
{
    tracon.AddAgent(new AgentDefinition
    {
        Name = "voice-assistant",
        DisplayName = "Voice Assistant",
        Description = "Speaks its answer on request; transcribes an attached voice recording.",
        Instructions = "You are a support assistant. Answer briefly. " +
                       "If the user asks you to speak, call the `speak` tool. " +
                       "If asked which voices are available, call the `list_voices` tool.",
        Model = new ModelBinding
        {
            Provider = OpenAIProviderNames.ChatCompletions,
            Model = openAi["DefaultModel"] ?? "gpt-5.4-mini",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["speak", "transcribe", "list_voices", "get_order_status"],
    });
}

// Knowledge base assistant. `EnableVectorSearch` only works when an
// IVectorSearchStore (today only PostgreSQL: UsePostgreSql()) AND an
// IEmbeddingGenerator are both registered; if either is missing, the build
// stops with an explicit error. Document upload is an ADMIN operation
// (POST /tracon/api/knowledge/{collection}/documents), not something the
// agent does itself — see docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md, 51.6.
if (openAiEnabled)
{
    tracon.AddAgent(new AgentDefinition
    {
        Name = "knowledge-assistant",
        DisplayName = "Knowledge Assistant",
        Description = "Answers questions using semantic search over the company knowledge base.",
        Instructions = "You are a company knowledge assistant. Before answering a question, " +
                       "always use the search_knowledge tool; base your answer only on the " +
                       "information the tool returns.",
        Model = model,
        Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "knowledge-base" },
    });
}

// Persistence is optional. Without a connection string, the app runs with
// in-memory stores; nothing breaks, data simply ends with the process.
//
// 🚨 TWO PROVIDERS ARE NEVER REGISTERED AT THE SAME TIME. If both are
// registered, the last call wins, and which database the data goes to
// depends on call order; Tracon logs this as a startup warning (K-183).
// The sample therefore deliberately chooses a single provider.
var postgreSql = builder.Configuration.GetSection(TraconPostgreSqlOptions.SectionName);
var sqlServer = builder.Configuration.GetSection(TraconSqlServerOptions.SectionName);
var sqlite = builder.Configuration.GetSection(TraconSqliteOptions.SectionName);

// The active provider and persistence status are now read from
// GET /tracon/api/diagnostics; the sample does not keep a separate flag.
if (!string.IsNullOrWhiteSpace(sqlServer["ConnectionString"]))
{
    tracon.UseSqlServer(sqlServer);
}
else if (!string.IsNullOrWhiteSpace(postgreSql["ConnectionString"]))
{
    tracon.UsePostgreSql(postgreSql);
}
else if (!string.IsNullOrWhiteSpace(sqlite["ConnectionString"]))
{
    tracon.UseSqlite(sqlite);
}

// Data retention and archiving. If IArchiveSink is NOT registered, a policy
// with archive=true deletes no rows (K-007: no cloud SDK dependency is taken).
// This sample is a template that writes to the file system; in a real setup,
// derive your own S3/Blob sink from this. It is only registered if a root
// path is explicitly given in configuration.
var archivePath = builder.Configuration["Tracon:Retention:ArchivePath"];

if (!string.IsNullOrWhiteSpace(archivePath))
{
    builder.Services.AddSingleton<IArchiveSink>(new FileSystemArchiveSink(archivePath));
}

// Multi-tenancy is optional and OFF BY DEFAULT. When turned on, the tenant is
// resolved from a claim first, and only from a header if explicitly allowed.
// A header can be spoofed; the setup below is for the sample only and stays
// inactive unless explicitly turned on in configuration.
if (builder.Configuration.GetValue<bool>("Tracon:Tenancy:Enabled"))
{
    tracon.UseTenancy(options =>
    {
        options.Enabled = true;
        options.ClaimType = builder.Configuration["Tracon:Tenancy:ClaimType"];
        options.AllowHeaderResolution =
            builder.Configuration.GetValue<bool>("Tracon:Tenancy:AllowHeaderResolution");
    });
}

// Health checks and diagnostics. Connects to the standard .NET health check
// system; this replaces the old hand-written /health body — the same
// information (persistence, provider status) is now collected in one place
// by TraconDiagnosticsCollector.
builder.Services.AddHealthChecks().AddTraconHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Three states: Healthy / Degraded / Unhealthy. For a detailed summary:
// GET /tracon/api/diagnostics (Admin, turned on below with EnableDiagnosticsEndpoint).
app.MapHealthChecks("/health");

// wwwroot/live-test.html: the smallest page that proves the provider-hosted live
// voice path end to end — getUserMedia, one RTCPeerConnection, one fetch. It is a
// SAMPLE asset and is not packaged; the Tracon.UI panel is a separate concern.
app.UseStaticFiles();

app.MapOpenApi();

// Swagger UI: renders the document MapOpenApi() serves at /openapi/v1.json.
// A browsable reference for this sample's HTTP surface; no Tracon package
// depends on this — a consumer picks their own OpenAPI viewer.
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Tracon API v1");
    options.RoutePrefix = "swagger";
});

// The single entry point. The management API (/tracon/api/*), the
// OpenAI-compatible run endpoints (/tracon/v1/*), and the embedded UI
// (/tracon) are all wired up by this one call.
//
// Access is restricted to loopback by default. In production, an
// authorization policy is attached:
//     options.RequireAuthorization("TraconAdmin");
//
// The bearer token is read only from secrets; it is NEVER written to
// appsettings.json:
//     dotnet user-secrets set "Tracon:Ui:AuthToken" "..."
app.MapTracon("/tracon", options =>
{
    if (builder.Configuration["Tracon:Ui:AuthToken"] is { Length: > 0 } token)
    {
        options.AuthToken = token;
    }

    // 🚨 The Tracon:Ui:AllowRemoteAccess key in appsettings.json was
    // previously NEVER wired up here — the value had no effect even though it
    // was present (a documentation/schema inconsistency only; not a security
    // hole, since the default was already the safe 'false').
    if (builder.Configuration.GetValue<bool?>("Tracon:Ui:AllowRemoteAccess") is { } allowRemoteAccess)
    {
        options.AllowRemoteAccess = allowRemoteAccess;
    }

    // CORS (Phase 61): empty by default (K1). Enables the embeddable chat
    // widget to be hosted on a different origin than this API.
    foreach (string origin in builder.Configuration.GetSection("Tracon:Ui:AllowedOrigins").Get<string[]>() ?? [])
    {
        options.AllowedOrigins.Add(origin);
    }

    // The diagnostics endpoint is OFF by default (K1: a surface that reveals
    // information is turned on explicitly). It is turned on here for
    // demonstration; even without the Admin role registered, it still goes
    // through the three-layer guard (loopback + bearer token).
    options.EnableDiagnosticsEndpoint = true;

    // 🚨 The gate that keeps the demo honest: when the role names are meant to
    // be active, a missing registration must FAIL AT STARTUP instead of
    // silently turning every RequireRole(...) into a no-op. If someone later
    // removes the AddPolicy calls above, this application no longer starts.
    options.RequireRolePolicies = demoRolesEnabled;
});

// MCP/A2A external surfaces. They inherit the SAME access protection as
// MapTracon (loopback + bearer token + authorization policy); calling
// them AFTER it is required.
app.MapTraconMcpServer();
app.MapTraconA2A();

app.Run();
