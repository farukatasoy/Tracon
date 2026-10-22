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
// See Phase 4, Phase 5, Phase 6,
//     Phase 8, Phase 12,
//     Phase 15, Phase 26,
//     Phase 27
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
using CommunityToolkit.VectorData.PgVector;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.VectorData;
using OpenAI;
using Tracon;
using Tracon.Api;

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

// Run authorization (phase 154). Tracon ships AllowAllRunAuthorizationHandler
// by default, so without this block the seam is invisible here: every run is
// allowed and no case can show one being refused. The alternative was to
// hand-edit this file for a test and undo the edit afterwards, which is not a
// mechanism — the next person has to rediscover it, and a forgotten edit ships.
//
// 🚨 Registered BEFORE AddTracon() for the same reason as IRunAttributionContext
// above: TryAddSingleton must not overwrite it. The identity it compares
// against comes from the X-Demo-User header (DemoRunAttributionContext).
//
// An unrecognized value STOPS the application instead of falling back to the
// allow-all default: a misspelled mode that quietly allowed everything would
// turn a refusal test green for the wrong reason.
if (builder.Configuration[DemoRunAuthorization.ModeKey] is { Length: > 0 } demoAuthorizationMode)
{
    var parsedMode = demoAuthorizationMode.Trim().ToLowerInvariant() switch
    {
        "deny-all" => DemoRunAuthorizationMode.DenyAll,
        "allow-user-a" => DemoRunAuthorizationMode.AllowUserA,
        "throw" => DemoRunAuthorizationMode.Throw,
        "deny-foreign-session" => DemoRunAuthorizationMode.DenyForeignSession,
        "deny-session-read" => DemoRunAuthorizationMode.DenySessionRead,
        "deny-session-list" => DemoRunAuthorizationMode.DenySessionList,
        "deny-session-delete" => DemoRunAuthorizationMode.DenySessionDelete,
        "deny-session-branch" => DemoRunAuthorizationMode.DenySessionBranch,
        "deny-session-voice" => DemoRunAuthorizationMode.DenySessionVoice,
        _ => throw new InvalidOperationException(
            $"'{DemoRunAuthorization.ModeKey}' is '{demoAuthorizationMode}', which is not a known "
            + "mode. Use one of: deny-all, allow-user-a, throw, deny-foreign-session, "
            + "deny-session-read, deny-session-list, deny-session-delete, deny-session-branch, "
            + "deny-session-voice."),
    };

    builder.Services.AddSingleton<IRunAuthorizationHandler>(provider =>
        new DemoRunAuthorization(
            parsedMode,
            provider.GetRequiredService<ILogger<DemoRunAuthorization>>()));
}

// Tool-approval presentation (phase 142): turns the cancel_order approval
// card's raw `{ "orderId": "ORD-1001" }` into "Cancel order for Priya Shah".
// Registered BEFORE AddTracon() for the same reason as
// IRunAttributionContext above — TryAddSingleton must not overwrite it.
builder.Services.AddSingleton<IToolApprovalPresenter, OrderApprovalPresenter>();

// 🚨 Tool authorization (phase 69). Tracon allows every tool call unless a
// consumer refuses one, so this seam is invisible here by default. Same
// reasoning as Tracon:Demo:RunAuthorization:Mode (K-834): a refusal that can
// only be produced by editing this file and undoing the edit is not a
// mechanism. `deny-all` refuses every call.
if (builder.Configuration["Tracon:Demo:ToolAuthorization:Mode"] is { Length: > 0 } toolAuthMode)
{
    if (!string.Equals(toolAuthMode.Trim(), "deny-all", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"'Tracon:Demo:ToolAuthorization:Mode' is '{toolAuthMode}', which is not a known mode. "
            + "The only supported value is: deny-all.");
    }

    builder.Services.AddSingleton<IToolAuthorizationHandler, DemoDenyAllToolAuthorization>();
}

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
        // 🚨 Which agents are exposed is a SECURITY decision, so the default is
        // the single agent this sample means to publish. The configuration key
        // exists for the same reason as Tracon:Demo:RunAuthorization:Mode
        // (K-834): the guard that refuses to expose an agent carrying an
        // approval-requiring tool can only be OBSERVED by exposing a different
        // agent, and a line that has to be edited and put back is not a
        // mechanism. An empty or absent key keeps the safe default.
        foreach (var name in DemoExposedAgents(builder.Configuration, "Mcp"))
        {
            o.ExposedAgents.Add(name);
        }

        o.EnableTasks = true;
    })
    .UseA2A(o =>
    {
        foreach (var name in DemoExposedAgents(builder.Configuration, "A2A"))
        {
            o.ExposedAgents.Add(name);
        }
    })
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
    // 🚨 The three calls below are normally unconditional. They are wrapped
    // because each one is a SEAM whose absence has its own documented answer
    // — a 501 naming the missing call, a route that does not exist at all, a
    // startup failure naming the three registration options — and none of
    // those answers can be observed while the call is always made.
    //
    // `Tracon:Demo:SuppressRegistrations` takes a comma-separated list of the
    // names below and skips those registrations. It follows K-834: a line that
    // has to be commented out and put back is not a mechanism. An empty or
    // absent key changes nothing.
    var suppressed = DemoSuppressedRegistrations(builder.Configuration);

    if (!suppressed.Contains("OpenAILive"))
    {
        tracon.UseOpenAILive(
            builder.Configuration.GetSection(OpenAILiveOptions.SectionName));
    }

    if (!suppressed.Contains("LiveVoice"))
    {
        tracon.UseLiveVoice(
            builder.Configuration.GetSection(VoiceLiveOptions.SectionName));
    }

    // Image generation is separately enabled by Tracon:Images. Registering
    // this keyed adapter alone does not expose generate_image or map its endpoint.
    if (!suppressed.Contains("OpenAIImages"))
    {
        tracon.UseOpenAIImages();
    }

    // OpenAI speech as NAMED speech providers, next to the default UseVoice() binds (F-579).
    // A caller selects the synthesizer with "provider": "openai":
    //
    //   POST /tracon/api/voice/speak  {"text":"Merhaba","provider":"openai"}
    //
    // The transcriber is reached through ISpeechProviderResolver.GetTranscriberAsync("openai");
    // there is no HTTP route for transcription. Every call passes the tenant's provider checks
    // first, against the provider name "openai" the chat provider uses: a tenant whose egress
    // policy does not name it gets 403, and a tenant with its own OpenAI key is refused for
    // speech (the client was built with this application's key).
    tracon.UseOpenAISpeech(options =>
    {
        options.SynthesisModel = "gpt-4o-mini-tts";
        options.TranscriptionModel = "gpt-4o-mini-transcribe";
    });

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

// A model that takes its time (phase 196), for showing a step's own timeout against
// a model that has not answered yet: `Tracon:Demo:SlowModel:Seconds=20` registers the
// provider "slow" (model "slow-1"), which answers after that many seconds and stops
// when its token is cancelled. Off unless the key is set, whatever provider is on.
if (builder.Configuration.GetValue<int?>("Tracon:Demo:SlowModel:Seconds") is { } slowSeconds)
{
    tracon.AddModelProvider(new SlowModelProvider(TimeSpan.FromSeconds(slowSeconds)));
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

// A NAMED speech synthesizer, next to the default one UseVoice() binds. "tone" needs
// no key: it beeps once per word and returns a WAV file, the way a host's own GPU
// text-to-speech server would answer. A caller selects it by name:
//
//   GET  /tracon/api/voice/providers
//   POST /tracon/api/voice/speak  {"text":"Merhaba","provider":"tone"}
//
// Every speech call passes the tenant's provider checks first. A tenant whose egress
// policy does not name "tone" gets 403 and the synthesizer is not called; a local
// engine is not exempt (PUT /tracon/api/tenants/default/egress).
tracon.AddSpeechSynthesizer(ToneSpeechSynthesizer.Name, static _ => new ToneSpeechSynthesizer());

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
// null — this is not a bug (see Phase 8, section
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
// environment variable (see Phase 49, DoD).
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
            ToolLoop = new ToolLoopSettings { AllowConcurrentToolCalls = true },
        },
        ToolNames = ["get_order_status", "list_recent_orders"],
    });

// A loop stop criterion created from the application's services (Phase 201).
// The factory takes the agent catalog: it runs on the first compilation of an
// agent that names the kind, never at setup, where the catalog is still being
// built. The console lists the kind with this description (GET
// /api/loop-criteria), and an operator can put it into any agent's loop.
// "reviewed-writer" below uses it; with no OpenAI key both agents run on the
// local echo provider, whose reviewer fails round 1 and passes round 2.
#pragma warning disable MAAI001 // The loop types are "for evaluation purposes only".
tracon.AddLoopEvaluator(
    DraftReviewCriterion.Kind,
    static services => new DraftReviewCriterion(services.GetRequiredService<IAgentCatalog>()),
    "The draft-reviewer agent approves the draft. A word count and a heading check are computed in code first.");
#pragma warning restore MAAI001

// An agent extension (Phase 222): code the host registers once and a definition
// selects by name. The console lists it in the agent editor (GET
// /api/agent-extensions), and "house-style-demo" below names it. The agent keeps
// the model ring, the tenant's provider credential, egress and cost, because it
// still goes through the definition compiler.
tracon.AddAgentExtension<HouseStyleExtension>();

// An agent extension with a chat client ring (Phase 232): the ring sits inside the
// tool-call loop and logs every model call of "house-style-demo" below.
tracon.AddAgentExtension<ModelCallLoggingExtension>();

tracon
    .AddAgent(new AgentDefinition
    {
        Name = DraftReviewCriterion.ReviewerAgent,
        DisplayName = "Draft Reviewer",
        Description = "Reviews a draft and answers with a JSON verdict.",
        Instructions = "Review the draft after the '---' line. The lines before it carry checks computed in code. " +
                       "Answer ONLY with a JSON object: {\"decision\": \"pass\" or \"fail\", " +
                       "\"findings\": [{\"issue\": string, \"fix\": string}]}. Pass a draft that starts with a " +
                       "'# ' heading and answers the request; list one finding per problem otherwise.",
        Model = openAiEnabled
            ? model with
            {
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    SchemaName = "review_verdict",
                    Schema = JsonDocument.Parse(
                        """
                        {
                          "type": "object",
                          "properties": {
                            "decision": { "type": "string", "enum": ["pass", "fail"] },
                            "findings": {
                              "type": "array",
                              "items": {
                                "type": "object",
                                "properties": { "issue": { "type": "string" }, "fix": { "type": "string" } },
                                "required": ["issue", "fix"],
                                "additionalProperties": false
                              }
                            }
                          },
                          "required": ["decision", "findings"],
                          "additionalProperties": false
                        }
                        """).RootElement.Clone(),
                },
            }
            : new ModelBinding { Provider = "echo", Model = EchoModelProvider.EchoReviewerModel },
    })
    .AddAgent(new AgentDefinition
    {
        Name = "reviewed-writer",
        DisplayName = "Reviewed Writer (loop demo)",
        Description = "Writes a short draft and revises it until the draft-reviewer agent approves it.",
        Instructions = "Write a short draft that answers the request. Start it with a '# ' heading. " +
                       "When you receive reviewer findings, revise the draft to fix every one.",
        Model = model,
        Harness = new HarnessSettings
        {
            // No harness tools: the loop needs none, and the echo model calls none.
            DisableTodoProvider = true,
            DisableAgentSkillsProvider = true,
            DisableAgentModeProvider = true,
            Loop = new LoopSettings
            {
                Criteria = [new LoopCriterion { Kind = DraftReviewCriterion.Kind }],
                MaxIterations = 3,
                OnExhausted = LoopOutcome.FailRun,
            },
        },
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
            // (see Phase 15).
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
    static services => BuildSummarizeAndApprove(services, "summarize-and-approve"),
    "Summarizes text, then waits for human approval to publish. Defined in code.");

// The same graph, taking ONE answer per request (phase 208). A double click or a
// retried call with the same answer follows the run the first answer started;
// a different answer is refused with 409. Every answer that starts a run is
// written to the audit trail ('workflow.respond') before the run starts. The
// registration above keeps the default (Branch): every answer opens a new run.
tracon.AddWorkflow(
    "summarize-and-approve-once",
    static services => BuildSummarizeAndApprove(services, "summarize-and-approve-once"),
    static options =>
    {
        options.Description = "Summarizes text, then waits for one approval to publish. Defined in code.";
        options.ResponseMode = WorkflowResponseMode.SingleAnswer;
    });

// The same graph with a time limit on the request (phase 209). A run that waits for a
// person keeps its request, checkpoints and input until somebody answers; a limit lets a
// request nobody answers close its run, so retention can release what it kept. The limit
// is two minutes here on purpose, so the expiry can be watched: GET /api/workflows/requests
// shows 'expiresAt', and two minutes later the run is Failed with the error type
// WorkflowRequestExpired. A real workflow would allow hours or days.
tracon.AddWorkflow(
    "summarize-and-approve-timed",
    static services => BuildSummarizeAndApprove(services, "summarize-and-approve-timed"),
    static options =>
    {
        options.Description = "Summarizes text, then waits two minutes for an approval to publish. Defined in code.";
        options.RequestTimeout = TimeSpan.FromMinutes(2);
    });

// A graph with a way BACK and a human gate (phase 211): a writer agent, a code check of its draft, a
// human review that can send the draft back with a note (a bounded loop), then three media nodes behind the
// approval. The graph is built with WorkflowBuilder in ReviewAndReviseWorkflow.cs and takes its steps from
// GetWorkflowAgentStep and GetWorkflowFunction, so the writer and the nodes have the timeout, retry, budget
// gate, concurrency limit, cost line and strict settings a stored chain has. The run's parameters are the
// production's identity (productionId) and the language. Start it queued and answer the review:
//
//   POST /tracon/api/workflows/review-and-revise/run   (Prefer: respond-async)
//   {"message":"start","parameters":{"productionId":"p-1","language":"en"}}
//   GET  /tracon/api/workflows/requests?state=pending
//   POST /tracon/api/workflows/runs/{runId}/respond
//   {"requestId":"...","data":{"approved":false,"note":"Make it shorter."}}
//
// A brief that contains "#long" makes the first draft fail the code check and go back to the writer on its own.
// The request limit and the run timeout can be changed to watch the expiry and the startup check:
//   Tracon:Demo:ReviewAndRevise:RequestTimeout=00:01:00   (a review nobody answers fails the run in a minute)
//   Tracon:Demo:ReviewAndRevise:RunTimeout=02:00:00       (above Tracon:Workflows:MaxRunTimeout: the host does not start)
//   Tracon:Demo:ReviewAndRevise:RenderSeconds=30          (a render long enough to stop the process in the middle of it)
tracon.AddReviewAndRevise(
    openAiEnabled
        ? model with
        {
            ResponseFormat = new AgentResponseFormat
            {
                Kind = AgentResponseFormatKind.JsonSchema,
                SchemaName = "episode_script",
                Schema = JsonDocument.Parse(
                    """
                    {
                      "type": "object",
                      "properties": { "title": { "type": "string" }, "script": { "type": "string" } },
                      "required": ["title", "script"],
                      "additionalProperties": false
                    }
                    """).RootElement.Clone(),
            },
        }
        : new ModelBinding { Provider = "echo", Model = EchoModelProvider.EchoWriterModel },
    requestTimeout: builder.Configuration.GetValue<TimeSpan?>("Tracon:Demo:ReviewAndRevise:RequestTimeout") ?? TimeSpan.FromHours(1),
    runTimeout: builder.Configuration.GetValue<TimeSpan?>("Tracon:Demo:ReviewAndRevise:RunTimeout") ?? TimeSpan.FromMinutes(5),
    renderSeconds: builder.Configuration.GetValue<int?>("Tracon:Demo:ReviewAndRevise:RenderSeconds") ?? 2);


// A code function usable as a workflow node (phase 71). AddWorkflowFunction
// registers the function by NAME; a Sequential definition's `nodes` list can
// then mix it in with catalog agents (K2: only the name crosses into the
// definition, the body always lives in code - the same shape
// AgentDefinition.ToolNames already uses for tools). This particular node
// makes NO model call - it demonstrates the phase's own motivation: a real
// pipeline has steps (formatting, counting, file I/O) that are not AI calls
// and previously could not enter a workflow graph at all.
//
// A workflow whose "nodes" field uses this function is a stored definition
// like any agent-only Sequential workflow, just with a function mixed in:
// AddWorkflow() is for a free-form graph defined ENTIRELY in code. Build it in
// the console (Workflows > New workflow > Add function) or save it over HTTP
// with PUT /tracon/api/workflows/{name}.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
    "word-count",
    static _ => (messages, _, _) =>
    {
        var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                   ?? string.Empty;
        var count = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return new ValueTask<List<ChatMessage>>([new ChatMessage(ChatRole.User, $"{text}\n\n(word count: {count})")]);
    },
    static options =>
    {
        options.Description = "Appends a word count to the incoming text. Runs no model call.";

        // A host that sets Tracon:AgentGraph:MaxTotalCost or MaxDuration stops a function node
        // from STARTING once the tree has spent it (F-407). This node spends nothing, so it may
        // still run after a costly one - say, a step that stores what the budget has just paid
        // for. The exemption is the node's code's to give; an operator cannot set it.
        options.StartsWhenBudgetIsSpent = true;
    });

// Parameter values reach a workflow's steps. A stored workflow declares a
// `parameters` schema (PUT /tracon/api/workflows/podcast); a run supplies
// values, and each agent step binds the ones ITS OWN schema names into its
// instructions. A workflow defined in code declares its schema in its registration
// (WorkflowRegistrationOptions.Parameters; see review-and-revise below).
//
//   podcast-writer  - {{tone}}, required
//   podcast-editor  - {{tone}} (default "plain") and {{voice}} (default "alloy")
//   narrate         - a function node that reads the run's values with
//                     context.GetRunParameters() and prefixes the "voice"
//
// The two agents answer with the instructions they were bound with, so the
// effect of a value is visible without a real model. They exist only while the
// local echo provider is the model (no OpenAI key configured).
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
    "narrate",
    static _ => (messages, context, _) =>
    {
        var voice = context.GetRunParameters().GetValueOrDefault("voice", "plain");
        var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                   ?? string.Empty;

        return new ValueTask<List<ChatMessage>>([new ChatMessage(ChatRole.User, $"[{voice}] {text}")]);
    },
    "Prefixes the text with the run's 'voice' parameter. Runs no model call.");

// A function with SETTINGS (F-295). The function's code declares the shape
// (SpeakSettings); an operator writes values for it on the workflow's node, in the
// console or over HTTP, so ONE registration serves several workflows with a
// different voice in each:
//
//   PUT /tracon/api/workflows/narrated
//   {"kind":"Sequential","nodes":[
//     {"name":"support","kind":"Agent"},
//     {"name":"speak","kind":"Function","settings":{"voice":"alloy","loudnessLufs":-14}}]}
//
// The settings are checked when the workflow is saved - an unknown field, a missing
// "voice" or a value of the wrong type answers 400 naming the node and the member -
// and again when a run starts. A change takes effect on the NEXT run. 🚨 Settings
// are stored in the definition and shown to every reader of the workflow: never put a
// secret in them. Name its configuration key instead (see SpeakSettings).
//
// 💰 A node reports what it spent beyond tokens (phase 199): characters spoken, GPU
// seconds, minutes of audio. Tracon does not price it: the node knows its provider's
// rate and reports the AMOUNT. It joins the run's non-token total, which the budget,
// the tenant's quota and the run.completed webhook count beside the token cost:
//
//   GET /tracon/api/runs/{runId}/usage      the items each node reported
//   GET /tracon/api/runs/{runId}            "nonTokenCost" / "treeNonTokenCost"
//
// `speak` makes no sound, so it bills what the local "tone" engine would charge for the
// same text (Tracon:Pricing:Voice:tone:tone-1) and says the quantity is an estimate.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, SpeakSettings>(
    "speak",
    static provider =>
    {
        var rates = TonePricing.From(provider);

        return async (messages, settings, context, cancellationToken) =>
        {
            var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                       ?? string.Empty;

            await context.ReportUsageAsync(rates.Characters(text.Length, isEstimated: true));

            return
            [
                new ChatMessage(
                    ChatRole.User,
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{text}\n\n[spoken by {settings.Voice} at {settings.LoudnessLufs} LUFS]")),
            ];
        };
    },
    static options => options.Description = "Pretends to read the text aloud: makes no sound and no model call, and reports the characters as an estimated, priced item. The voice and the loudness come from the node's settings.");

// A SLOW step, for queued runs and their recovery (phase 197). It stands in for a
// render that takes minutes, so a run can be stopped halfway through it:
//
//   PUT /tracon/api/workflows/episode
//   {"kind":"Sequential","nodes":[
//     {"name":"word-count","kind":"Function"},
//     {"name":"render","kind":"Function","settings":{"seconds":30}}]}
//
//   POST /tracon/api/workflows/episode/run   (Prefer: respond-async)
//
// Stop the process while the render waits, start it again, and with
// Tracon:Workflows:ResumeInterruptedRuns on the job continues the run from the
// checkpoint after word-count: a NEW run whose continuedFromRunId names the stopped
// one. The render runs again, the step before it does not. It honours the run's
// token, so a graceful stop ends it at once.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, RenderSettings>(
    "render",
    static _ => async (messages, settings, context, cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.Seconds, 0, 3600)), cancellationToken).ConfigureAwait(false);

        // The seconds the render held a GPU. No amount: this host has no rate for it, so
        // the item is listed as "unpriced" and left out of every total - unknown, not free.
        await context.ReportUsageAsync(new ToolCallUsage
        {
            Unit = "gpu-seconds",
            Quantity = Math.Clamp(settings.Seconds, 0, 3600),
            Provider = "local-gpu",
        });

        var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                   ?? string.Empty;

        return [new ChatMessage(ChatRole.User, $"{text}\n\n[rendered in {settings.Seconds} s]")];
    },
    static options => options.Description = "Pretends to render the text: waits the node's 'seconds' setting, makes no model call and reports the seconds as an unpriced GPU item.");

// Two functions with a TIMEOUT of their own (phase 196). `slow` waits ten seconds and
// passes its token on, so its two-second timeout cancels it: the run ends Failed with
// the error type workflow_function_timeout and no later node runs. A node can set its
// own value, which wins over the registration's:
//
//   PUT /tracon/api/workflows/bounded
//   {"kind":"Sequential","nodes":[
//     {"name":"word-count","kind":"Function"},
//     {"name":"slow","kind":"Function","timeout":"00:00:05"}]}
//
// `deaf` ignores its token, as a careless handler would: the run still stops waiting at
// the timeout, the body keeps running in the background, and its late end is logged
// ("returned ... past its timeout"). Pass the token on.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
    "slow",
    static _ => async (messages, _, cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

        return messages;
    },
    static options =>
    {
        options.Description = "Waits ten seconds, honouring its token. Registered with a two-second timeout.";
        options.Timeout = TimeSpan.FromSeconds(2);
    });

tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
    "deaf",
    static _ => async (messages, _, _) =>
    {
        // Ignores its token on purpose; see the comment above.
        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None).ConfigureAwait(false);

        return messages;
    },
    static options =>
    {
        options.Description = "Waits six seconds and IGNORES its token. Registered with a two-second timeout.";
        options.Timeout = TimeSpan.FromSeconds(2);
    });

// A function node that REALLY speaks, with the engine its settings name:
//
//   PUT /tracon/api/workflows/narrated-tone
//   {"kind":"Sequential","nodes":[
//     {"name":"support","kind":"Agent"},
//     {"name":"synthesize","kind":"Function","settings":{"provider":"tone","voice":"high"}}]}
//
// The resolver is taken when the node is built - the service provider a node sees at
// call time is not the host's. It checks the RUN's tenant, so a queued run is judged
// by the tenant that queued it. A refusal fails the node and is not retried.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, SynthesizeSettings>(
    "synthesize",
    static provider =>
    {
        var speech = provider.GetRequiredService<ISpeechProviderResolver>();
        var attachments = provider.GetRequiredService<IAttachmentStore>();
        var rates = TonePricing.From(provider);

        return async (messages, settings, context, cancellationToken) =>
        {
            var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                       ?? string.Empty;
            var scope = TraconRunContext.Current
                ?? throw new InvalidOperationException("The synthesize node runs only inside a Tracon run.");

            var synthesizer = await speech.GetSynthesizerAsync(settings.Provider, cancellationToken);
            var audio = await synthesizer.SynthesizeAsync(
                new SpeechRequest { Text = text, VoiceId = settings.Voice },
                cancellationToken);

            // The synthesis is billed whether or not the attachment below is saved, so it
            // is reported first. The engine's own count wins over the text's length.
            await context.ReportUsageAsync(rates.Characters(
                audio.CharactersBilled ?? text.Length,
                isEstimated: audio.UsageSource != SpeechUsageSource.Provider,
                provider: synthesizer.ProviderName,
                model: audio.ModelId));

            var saved = await attachments.SaveAsync(
                new AttachmentContent
                {
                    TenantId = scope.TenantId ?? "default",
                    SessionId = scope.SessionId,
                    RunId = scope.RunId,
                    FileName = "speech.wav",
                    MediaType = audio.MediaType,
                    Data = audio.Data,
                    CreatedBy = scope.AgentName,
                },
                cancellationToken);

            return
            [
                new ChatMessage(
                    ChatRole.User,
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{text}\n\n[spoken by {synthesizer.ProviderName}: attachment {saved.Id}, {audio.Data.Length} bytes]")),
            ];
        };
    },
    static options => options.Description = "Speaks the text with the speech synthesizer the node's settings name and saves the audio as an attachment.");

// A function node that voices a DIALOGUE (phase 203): each non-empty line of the last
// message is one turn, and the voices in the settings take turns in order. With the
// ElevenLabs key configured, the default synthesizer voices it:
//
//   PUT /tracon/api/workflows/two-voices
//   {"kind":"Sequential","nodes":[
//     {"name":"dialogue","kind":"Function","settings":{
//        "voices":["onwK4e9ZLuTAKqWW03F9","TX3LPaxmHKxFdv7VOQHJ"],
//        "modelId":"eleven_v4","languageCode":"tr","outputFormat":"pcm_24000",
//        "includeTimestamps":true,
//        "providerSettings":{"elevenlabs.stability":0.3}}}]}
//
//   POST /tracon/api/workflows/two-voices/run   {"message":"Merhaba.\nSelam."}
//
// The capability comes from the resolver, after the run tenant's checks: there is no
// container registration to call it past them. A request over the engine's limits, an
// unknown provider setting or an engine that cannot voice a dialogue ("provider":"tone")
// fails the node before any audio is made, and the message names the limit, the key or
// the engine. Splitting a long script and joining the audio stay in this code: raw PCM
// is wrapped in a WAV header here so the attachment store can keep it.
tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, DialogueSettings>(
    "dialogue",
    static provider =>
    {
        var speech = provider.GetRequiredService<ISpeechProviderResolver>();
        var attachments = provider.GetRequiredService<IAttachmentStore>();
        var rates = TonePricing.From(provider);

        return async (messages, settings, context, cancellationToken) =>
        {
            var text = messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                       ?? string.Empty;
            var scope = TraconRunContext.Current
                ?? throw new InvalidOperationException("The dialogue node runs only inside a Tracon run.");

            if (settings.Voices.Count == 0)
            {
                throw new TraconException("The dialogue node needs at least one voice in its settings.");
            }

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var turns = lines
                .Select((line, index) => new SpeechDialogueTurn { Text = line, VoiceId = settings.Voices[index % settings.Voices.Count] })
                .ToList();

            var dialogue = await speech.GetDialogueSynthesizerAsync(settings.Provider, cancellationToken);
            var result = await dialogue.SynthesizeDialogueAsync(
                new SpeechDialogueRequest
                {
                    Turns = turns,
                    ModelId = settings.ModelId,
                    LanguageCode = settings.LanguageCode,
                    OutputFormat = settings.OutputFormat,
                    IncludeTimestamps = settings.IncludeTimestamps,
                    ProviderSettings = settings.ProviderSettings ?? (IReadOnlyDictionary<string, System.Text.Json.JsonElement>)System.Collections.ObjectModel.ReadOnlyDictionary<string, System.Text.Json.JsonElement>.Empty,
                },
                cancellationToken);

            // The dialogue is ONE synthesis: its result reports like a single voice's. When
            // the settings name no model and the engine reports none, the price is looked up
            // without a model - a price keyed by a model is then not found and the item is
            // listed as unpriced, not priced at some other model's rate.
            await context.ReportUsageAsync(rates.Characters(
                result.Audio.CharactersBilled ?? text.Length,
                isEstimated: result.Audio.UsageSource != SpeechUsageSource.Provider,
                provider: dialogue.ProviderName,
                model: result.Audio.ModelId ?? settings.ModelId));

            var wav = PcmWave.Wrap(settings.OutputFormat, result.Audio.Data.Span);
            var saved = await attachments.SaveAsync(
                new AttachmentContent
                {
                    TenantId = scope.TenantId ?? "default",
                    SessionId = scope.SessionId,
                    RunId = scope.RunId,
                    FileName = wav is null ? "dialogue.mp3" : "dialogue.wav",
                    MediaType = wav is null ? result.Audio.MediaType : "audio/wav",
                    Data = wav ?? result.Audio.Data,
                    CreatedBy = scope.AgentName,
                },
                cancellationToken);

            var report = new System.Text.StringBuilder();
            report.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"[dialogue by {dialogue.ProviderName}: attachment {saved.Id}, {saved.ByteSize} bytes, ");
            report.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"{result.Audio.CharactersBilled} characters ({result.Audio.UsageSource}), model {result.Audio.ModelId ?? "provider default"}, ");
            report.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"request {result.Audio.ProviderRequestId ?? "none"}]");

            foreach (var segment in result.Segments ?? [])
            {
                report.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"\nturn {segment.TurnIndex} ({segment.VoiceId}): {segment.Start.TotalSeconds:0.00}-{segment.End.TotalSeconds:0.00} s");
            }

            return [new ChatMessage(ChatRole.User, report.ToString())];
        };
    },
    static options => options.Description = "Voices each line of the text as one turn of a dialogue, the voices taking turns, and saves the audio as an attachment.");

if (!openAiEnabled)
{
    var instructionsModel = new ModelBinding { Provider = "echo", Model = EchoModelProvider.EchoInstructionsModel };

    tracon
        .AddAgent(new AgentDefinition
        {
            Name = "podcast-writer",
            DisplayName = "Podcast Writer (parameters demo)",
            Description = "Writes an episode script in the requested tone.",
            Instructions = "Write the script in a {{tone}} tone.",
            Parameters = [new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, Required = true }],
            Model = instructionsModel,
        })
        .AddAgent(new AgentDefinition
        {
            Name = "podcast-editor",
            DisplayName = "Podcast Editor (parameters demo)",
            Description = "Edits a script for a tone and a voice.",
            Instructions = "Edit for a {{tone}} tone, voice {{voice}}.",
            Parameters =
            [
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
                new AgentParameter { Name = "voice", Kind = AgentParameterKind.Text, DefaultValue = "alloy" },
            ],
            Model = instructionsModel,
        })

        // The local echo model prints the instructions it received, so a run of this
        // agent shows the rule the house-style extension added.
        .AddAgent(new AgentDefinition
        {
            Name = "house-style-demo",
            DisplayName = "House Style (extension demo)",
            Description = "Answers with the house-style rule an agent extension adds.",
            Instructions = "Answer the question.",
            Model = instructionsModel,
            ExtensionNames = [HouseStyleExtension.ExtensionName, ModelCallLoggingExtension.ExtensionName],
        });
}

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
            // "gpt-5.4-mini" but "openai/gpt-5.4-mini". Measured: Phase 8.
            Model = openRouter["DefaultModel"] ?? "openai/gpt-5.4-mini",
            // OpenRouter's credit check treats max_tokens as a "worst case";
            // the default (65536) produces HTTP 402 on low-balance keys.
            // Measured: Phase 8.
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
// agent does itself — see Phase 51, 51.6.
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

// Session workspace (F-628). The agent lists, reads, searches, writes, edits and deletes files in a private
// area that belongs to ONE session: another session does not see them, they are not the agent's file memory,
// and they are deleted with the session (DELETE /api/sessions/{id}, an erasure of the data subject, or the
// retention period). A tool that changes a file always waits for a person to approve it; reading is allowed
// here without asking. The size limits are in Tracon:Workspace. A run with no session has no workspace.
if (openAiEnabled)
{
    tracon.AddAgent(new AgentDefinition
    {
        Name = "workspace-drafter",
        DisplayName = "Workspace Drafter",
        Description = "Drafts and edits files in a private workspace that belongs to the session.",
        Instructions = "You are a drafting assistant. Keep drafts and notes in files of your workspace, " +
                       "and read a file back before you change it. Answer briefly.",
        Model = model,
        Workspace = new WorkspaceSettings { Enabled = true, DisableReadApproval = true },
    });
}

// Conversation memory (F-627). The agent finds what the SAME user said to it in earlier sessions. The
// records live in a vector store this host registers: here the PgVector connector over the same
// PostgreSQL database, embedding with the embedding generator registered above (the store embeds; Tracon
// does not see that call). Scope is tenant + agent + user: the demonstration identity (X-Demo-User)
// decides who "the same user" is. A run with no user, or no session, remembers nothing.
if (openAiEnabled && builder.Configuration["Tracon:PostgreSql:ConnectionString"] is { Length: > 0 } memoryConnection)
{
    tracon.UseConversationMemory(options => options.VectorDimensions = 1536);

    builder.Services.AddSingleton<VectorStore>(provider => new PostgresVectorStore(
        memoryConnection,
        new PostgresVectorStoreOptions
        {
            Schema = builder.Configuration["Tracon:PostgreSql:SchemaName"],
            EmbeddingGenerator = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        }));

    tracon.AddAgent(new AgentDefinition
    {
        Name = "memory-assistant",
        DisplayName = "Memory Assistant",
        Description = "Remembers what the same user told it in earlier sessions.",
        Instructions = "You are a personal assistant. Answer briefly. When a message under Memories answers the question, use it.",
        Model = model,
        Memory = new MemorySettings { EnableConversationMemory = true },
    });
}

// Hosted tools (F-603): a web search and a code interpreter the model PROVIDER
// runs inside the model call. They are code, as every tool is: a definition
// selects them by name in ToolNames, and an agent that does not is not offered
// them. Tracon puts no timeout, approval or permission in front of a hosted
// tool, and the provider bills each call: the price comes from
// Tracon:Pricing:HostedTools:{provider}:{kind}:PerCall (sample figures in
// appsettings.json), so a call reaches the run's cost, the budget, the quota
// and the run.completed webhook.
//
// Whether a provider runs a kind depends on its API surface. "openai" (Chat
// Completions) runs none of them and "openai-responses" runs all three;
// POST /tracon/api/agents/validate says which agent asks a surface for a tool it
// would drop.
tracon
    .AddHostedTool("web_search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool(), "Searches the web.")
    .AddHostedTool(
        "code_interpreter",
        HostedToolKind.CodeInterpreter,
        static _ => new HostedCodeInterpreterTool(),
        "Runs code in the model provider's sandbox.");

if (openAiEnabled)
{
    tracon.AddAgent(new AgentDefinition
    {
        Name = "hosted-openai",
        DisplayName = "Hosted tools (OpenAI)",
        Description = "A web search and a code interpreter the OpenAI Responses API runs.",
        Instructions = "Answer briefly. Use the web search for anything recent and the code interpreter for arithmetic.",
        Model = new ModelBinding
        {
            Provider = OpenAIProviderNames.Responses,
            Model = openAi["DefaultModel"] ?? "gpt-5.4-mini",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["web_search", "code_interpreter"],
    });
}

if (anthropicEnabled)
{
    tracon.AddAgent(new AgentDefinition
    {
        Name = "hosted-claude",
        DisplayName = "Hosted tools (Claude)",
        Description = "A web search and a code interpreter Anthropic runs.",
        Instructions = "Answer briefly. Use the web search for anything recent and the code interpreter for arithmetic.",
        Model = new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = anthropic["DefaultModel"] ?? "claude-haiku-4-5-20251001",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["web_search", "code_interpreter"],
    });
}

if (googleEnabled)
{
    tracon.AddAgent(new AgentDefinition
    {
        Name = "hosted-gemini",
        DisplayName = "Hosted tools (Gemini)",
        Description = "A web search and a code interpreter Google runs. No price is configured for Google, so its calls are listed and not added to the cost.",
        Instructions = "Answer briefly. Use the web search for anything recent and the code interpreter for arithmetic.",
        Model = new ModelBinding
        {
            Provider = GoogleProviderNames.Google,
            Model = google["DefaultModel"] ?? "gemini-3.6-flash",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["web_search", "code_interpreter"],
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
// 🚨 The branch runs when the key is PRESENT, not only when it is true. The
// false case is its own documented behavior — UseTenancy(Enabled = false) is
// still a single-tenant deployment, and the production profile must still
// count it as one — and that cannot be observed if the call is skipped
// whenever the value is false (K-834's reasoning).
if (builder.Configuration["Tracon:Tenancy:Enabled"] is { Length: > 0 })
{
    tracon.UseTenancy(options =>
    {
        options.Enabled = builder.Configuration.GetValue<bool>("Tracon:Tenancy:Enabled");
        options.ClaimType = builder.Configuration["Tracon:Tenancy:ClaimType"];
        options.AllowHeaderResolution =
            builder.Configuration.GetValue<bool>("Tracon:Tenancy:AllowHeaderResolution");

        // 🚨 The option existed but this sample never bound it, so the
        // allow-list could not be exercised at all here: every tenant name
        // was accepted and the "a name outside the list does not fall back to
        // the default tenant" rule had nothing to refuse. Comma-separated;
        // an empty or absent key keeps the option empty, which means
        // "no allow-list" and is the previous behavior exactly.
        foreach (var name in (builder.Configuration["Tracon:Tenancy:AllowedTenants"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            options.AllowedTenants.Add(name);
        }
    });
}

// Production profile (phase 175). The gate refuses to start a host whose
// permissive decisions have not been looked at. It is OFF here because this is
// a sample — turning it on unconditionally would stop `dotnet run` for anyone
// exploring the repository.
//
// 🚨 A demonstration flag rather than a hand-edit, for the same reason as
// Tracon:Demo:RunAuthorization:Mode (K-834): a line that has to be added and
// removed again is not a mechanism. `Tracon:Demo:ProductionProfile:Accept`
// takes the comma-separated risk names to accept, so the "an accept covers
// only the risk it names" behavior can be shown without editing this file.
if (builder.Configuration.GetValue<bool>("Tracon:Demo:ProductionProfile:Enabled"))
{
    var accepted = (builder.Configuration["Tracon:Demo:ProductionProfile:Accept"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    tracon.RequireProductionProfile(profile =>
    {
        foreach (var name in accepted)
        {
            // An unknown name stops the host rather than being ignored: a typo
            // that silently accepted nothing would make the gate look stricter
            // than it is, and a typo that silently accepted everything would
            // make it look weaker. Neither failure is visible in the output.
            if (!Enum.TryParse<TraconProductionRisk>(name, ignoreCase: true, out var risk))
            {
                throw new InvalidOperationException(
                    $"'Tracon:Demo:ProductionProfile:Accept' names '{name}', which is not a "
                    + $"TraconProductionRisk. Known values: {string.Join(", ", Enum.GetNames<TraconProductionRisk>())}.");
            }

            profile.Accept(risk);
        }
    });
}

// Tool approval POLICY (code) versus approval RULES (data). The policy is
// consulted first and its Required/NotRequired answers override the stored
// rules; a policy that answers Undecided leaves the rules in charge. 🚨 The
// sample registers none by default, so the precedence — code beats data — is
// not observable here. Same reasoning as the other demo hooks (K-834).
//
// Format: comma-separated `tool=Decision`, for example
// `refund_order=Required`. An unknown tool name is fine (the policy simply
// never fires); an unknown DECISION stops the host, because a misspelling
// that silently became Undecided would make a precedence test pass against
// the data rule it was supposed to override.
foreach (var entry in (builder.Configuration["Tracon:Demo:ToolApprovalPolicy"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);

    if (parts.Length != 2 || !Enum.TryParse<ToolApprovalPolicyDecision>(parts[1], ignoreCase: true, out var decision))
    {
        throw new InvalidOperationException(
            $"'Tracon:Demo:ToolApprovalPolicy' entry '{entry}' is not `tool=Decision`. Known decisions: "
            + string.Join(", ", Enum.GetNames<ToolApprovalPolicyDecision>()));
    }

    tracon.AddToolApprovalPolicy(parts[0], _ => decision);
}

// The pattern content guard, registered separately from the chain above so it
// can be left out. 🚨 Emptying its rule set is NOT the same as not registering
// it, and that difference is exactly what the production profile measures: the
// UninspectedContent check asks whether an IContentGuard is REGISTERED, not
// whether an options flag is set. Leaving it out is the only way to see that
// check report, so it follows K-834 rather than a hand-edit.
if (!DemoSuppressedRegistrations(builder.Configuration).Contains("ContentGuard"))
{
    tracon.AddPatternContentGuard(options =>
    {
        options.MaskedPii = PiiPatterns.CreditCard | PiiPatterns.Email | PiiPatterns.ProviderApiKey;
        options.DeniedTerms.Add("confidential-project");
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

    // 🚨 A shipped option whose OFF position this sample could never SHOW.
    // MapOpenAIConversations is decided here, at MapTracon time, so the only
    // way to reach its false branch was a temporary edit of this file — the
    // exact thing K-834 refuses. The key binds the REAL option; it adds no
    // behavior of its own, and its absence leaves the shipped default (true).
    if (builder.Configuration.GetValue<bool?>("Tracon:Demo:MapOpenAIConversations") is { } mapConversations)
    {
        options.MapOpenAIConversations = mapConversations;
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
    // 🚨 Normally tied to the demo roles: when the names are meant to be
    // active, a missing registration must fail at startup. The override exists
    // because the OPPOSITE combination — the gate on with NO policy registered
    // — is its own documented behavior and cannot be reached otherwise
    // (turning the demo roles on registers the policies, which satisfies it).
    options.RequireRolePolicies =
        builder.Configuration.GetValue<bool?>("Tracon:Demo:RequireRolePolicies") ?? demoRolesEnabled;
});

// MCP/A2A external surfaces. They inherit the SAME access protection as
// MapTracon (loopback + bearer token + authorization policy); calling
// them AFTER it is required.
app.MapTraconMcpServer();
app.MapTraconA2A();

app.Run();

// The graph of the two approval workflows registered earlier. The workflow name is passed in:
// it is part of each participant agent's identity (GetWorkflowAgent).
static Workflow BuildSummarizeAndApprove(IServiceProvider services, string name)
{
    var port = RequestPort.Create<string, bool>("publish-approval");

    var summarize = services.GetWorkflowAgent(
        name,
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
        .WithName(name)
        .Build();
}

/// <summary>
/// The agent names the sample exposes over <paramref name="surface"/>.
/// </summary>
/// <param name="configuration">The application configuration.</param>
/// <param name="surface">Either <c>Mcp</c> or <c>A2A</c>.</param>
/// <returns>The configured names, or the sample's own default.</returns>
/// <remarks>
/// Reads <c>Tracon:Demo:ExposedAgents:{surface}</c> as a comma-separated list.
/// The default is <c>summarizer</c>, the one agent this sample publishes: it
/// carries no approval-requiring tool, so the startup guard lets it through.
/// </remarks>
static string[] DemoExposedAgents(IConfiguration configuration, string surface)
{
    var configured = configuration[$"Tracon:Demo:ExposedAgents:{surface}"];

    return string.IsNullOrWhiteSpace(configured)
        ? ["summarizer"]
        : configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// The registration calls the sample skips, from
/// <c>Tracon:Demo:SuppressRegistrations</c>.
/// </summary>
/// <param name="configuration">The application configuration.</param>
/// <returns>The suppressed names; empty when the key is absent.</returns>
/// <remarks>
/// 🚨 Demonstration only. Known names: <c>OpenAILive</c>, <c>LiveVoice</c>,
/// <c>OpenAIImages</c>. An unknown name stops the host rather than being
/// ignored — a typo that suppressed nothing would make a case measuring the
/// "missing registration" answer pass against the registration that is still
/// there.
/// </remarks>
static HashSet<string> DemoSuppressedRegistrations(IConfiguration configuration)
{
    string[] known = ["OpenAILive", "LiveVoice", "OpenAIImages", "ContentGuard"];

    var configured = (configuration["Tracon:Demo:SuppressRegistrations"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var name in configured)
    {
        if (!known.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'Tracon:Demo:SuppressRegistrations' names '{name}', which is not a known "
                + $"registration. Known values: {string.Join(", ", known)}.");
        }

        result.Add(name);
    }

    return result;
}
