// AgentPrism embedding sample.
//
// samples/AgentPrism.Api is a GREEN-FIELD setup: it shows the two-line promise
// (AddAgentPrism() + MapAgentPrism()) working on its own. This sample shows the
// OTHER promise: an application that already has its own tenants, users,
// permissions, event bus, and object storage, embedding AgentPrism into that
// existing shape instead of starting from an empty one. See
// docs-site/src/content/docs/guides/embedding.md for the full explanation this
// sample follows line by line.
//
// The five embedding points, all bound BEFORE AddAgentPrism() so the host's own
// registration wins over the built-in default (every contract uses TryAdd):
//   1. ITenantContext / ITenantStore -> Tenancy/
//   2. IRunAttributionContext        -> Attribution/
//   3. IToolAuthorizationHandler     -> Authorization/
//   4. IRunEventSink                 -> Events/ (bounded channel, drops on backpressure)
//   5. IAttachmentStorage            -> Attachments/
//
// Background work: Jobs/ runs an agent with NO HTTP request behind it, using
// AmbientTenantScope and AmbientRunAttributionScope — the mandatory scenario
// this sample exists to prove end to end.
//
// GET /agentprism/api/diagnostics (enabled below) reports all five as
// isBuiltInDefault: false, naming this sample's own types — contrast with
// samples/AgentPrism.Api, where the four points it never binds still report
// AgentPrism's own defaults.

using AgentPrism;
using AgentPrism.Embedded;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();

// The five embedding points. Order matters: AddAgentPrism() below calls
// TryAdd* for all five, so a registration made first wins; a registration
// made after AddAgentPrism() is silently ignored.
builder.Services.AddSingleton<ITenantContext, EmbeddedTenantContext>();
builder.Services.AddSingleton<ITenantStore, EmbeddedTenantStore>();
builder.Services.AddSingleton<IRunAttributionContext, EmbeddedRunAttributionContext>();
builder.Services.AddSingleton<IToolAuthorizationHandler, EmbeddedToolAuthorizationHandler>();
builder.Services.AddSingleton<RunEventBridgeState>();
builder.Services.AddSingleton<BoundedChannelRunEventSink>();
builder.Services.AddSingleton<IRunEventSink>(static provider => provider.GetRequiredService<BoundedChannelRunEventSink>());
builder.Services.AddHostedService<RunEventBridgeWorker>();
builder.Services.AddSingleton<IAttachmentStorage, InMemoryBufferAttachmentStorage>();

// The host's own background work queue and worker — see Jobs/EmbeddedJobWorker.cs
// for the AmbientTenantScope / AmbientRunAttributionScope pattern.
builder.Services.AddSingleton<EmbeddedJobQueue>();
builder.Services.AddHostedService<EmbeddedJobWorker>();

var agentPrism = builder.AddAgentPrism()
    .AddGeneratedTools()
    .AddModelProvider(new EchoModelProvider());

agentPrism.AddAgent(new AgentDefinition
{
    Name = "assistant",
    DisplayName = "Embedded Assistant",
    Description = "Answers account questions for the host's own tenants.",
    Instructions = "You are a support assistant embedded in a host application. Answer briefly.",
    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
    ToolNames = ["current_account", "account_balance", "delete_account"],
});

var app = builder.Build();

// The host's own endpoint, in the host's own style — not part of AgentPrism.
// Enqueues background work with no request behind it once it runs.
app.MapPost("/jobs", (EnqueueJobRequest request, EmbeddedJobQueue queue) =>
{
    queue.Enqueue(new EmbeddedJob(request.TenantId, request.UserId, request.Message));

    return Results.Accepted();
});

app.MapGet("/jobs/bridge-state", (RunEventBridgeState state) =>
    Results.Ok(new { received = state.Received, dropped = state.Dropped }));

// The single entry point for AgentPrism's own management API, OpenAI-compatible
// run endpoints, and (once turned on) diagnostics.
app.MapAgentPrism("/agentprism", options =>
{
    // Off by default (K1: a surface that reveals deployment shape is turned on
    // explicitly). Turned on here so the sample's own manual test case and
    // GET /agentprism/api/diagnostics can show the bound embedding points.
    options.EnableDiagnosticsEndpoint = true;
});

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in
// tests/AgentPrism.Embedded.Tests.
#pragma warning disable CA1050 // top-level statements generate this type in the global namespace
public partial class Program;
#pragma warning restore CA1050
