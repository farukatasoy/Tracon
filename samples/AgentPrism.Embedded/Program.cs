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
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

// Persistence is OPTIONAL (Phase 110). Without a connection string this sample
// keeps running fully in memory, exactly as before — CI never depends on a
// database. With one, the host's OWN schema (HostDbContext) and AgentPrism's
// store layer share a SINGLE NpgsqlDataSource: building two separate data
// sources from the same connection string would NOT share a pool (measured,
// see docs-site/.../guides/embedding.md). See
// docs-site/.../guides/ef-core.md for the full pattern this wiring runs.
var postgreSqlConnectionString = builder.Configuration
    .GetSection(AgentPrismPostgreSqlOptions.SectionName)[nameof(AgentPrismPostgreSqlOptions.ConnectionString)];
var hasPersistence = !string.IsNullOrWhiteSpace(postgreSqlConnectionString);
NpgsqlDataSource? dataSource = null;

if (hasPersistence)
{
    dataSource = new NpgsqlDataSourceBuilder(postgreSqlConnectionString).Build();

    builder.Services.AddDbContext<HostDbContext>(o => o.UseNpgsql(dataSource));
    agentPrism.UsePostgreSql(o => o.DataSource = dataSource);
}

var app = builder.Build();

if (hasPersistence)
{
    // AgentPrism never disposes a data source it did not build (see
    // AgentPrismPostgreSqlOptions.DataSource): ownership stays with whoever
    // built it, which is THIS application. Registering it for shutdown is
    // this host's own responsibility, exactly like it would be for any other
    // unmanaged resource it constructs by hand.
    app.Lifetime.ApplicationStopping.Register(() => dataSource!.Dispose());

    // Sample-only setup step: a real deployment applies its own schema with
    // `dotnet ef database update` as a separate deployment step (see
    // "Alongside your own EF Core migrations" in the production guide),
    // independently of `agentprism migrate` / AutoApplyMigrations.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HostDbContext>().Database.EnsureCreatedAsync();
}

// The host's own endpoint, in the host's own style — not part of AgentPrism.
// Enqueues background work with no request behind it once it runs.
app.MapPost("/jobs", (EnqueueJobRequest request, EmbeddedJobQueue queue) =>
{
    queue.Enqueue(new EmbeddedJob(request.TenantId, request.UserId, request.Message));

    return Results.Accepted();
});

app.MapGet("/jobs/bridge-state", (RunEventBridgeState state) =>
    Results.Ok(new { received = state.Received, dropped = state.Dropped }));

if (hasPersistence)
{
    // Runs the RunId-reference pattern the EF Core guide describes: the
    // caller generates the run's identifier UP FRONT (AgentPrismRunOptions.RunId)
    // so it can save a host-owned row that references the run without waiting
    // for — or copying — anything AgentPrism itself records for it.
    app.MapPost("/tickets", async (
        CreateTicketRequest request, HostDbContext db, IAgentCatalog catalog, CancellationToken cancellationToken) =>
    {
        var agent = await catalog.ResolveAsync("assistant", culture: null, cancellationToken).ConfigureAwait(false);

        if (agent is null)
        {
            return Results.Problem("The 'assistant' agent is not in the catalog.", statusCode: 503);
        }

        var runId = AgentPrismId.NewId();

        using (AmbientTenantScope.Begin(request.TenantId))
        using (AmbientRunAttributionScope.Begin(request.UserId, labels: null))
        {
            await agent.RunAsync(
                request.Subject,
                options: new AgentPrismRunOptions { RunId = runId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Subject = request.Subject,
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Created($"/tickets/{ticket.Id}", ticket);
    });
}

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
