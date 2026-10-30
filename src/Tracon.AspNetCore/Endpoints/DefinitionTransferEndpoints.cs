using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Definition export and import: move agents, workflows, skills and MCP servers between installations.</summary>
/// <remarks>
/// <para>
/// The import is a second writer of the four definitions, and it is not allowed to know a rule the save endpoints
/// do not: every item passes the same write rules (<c>WriteRules/*</c>), with the bundle laid over the target so a
/// reference between two items of the bundle is judged as it will be once both are saved.
/// </para>
/// <para>
/// <c>dryRun</c> defaults to <see langword="true"/>, the way the data subject erasure does: a bare call must not be
/// the one that writes.
/// </para>
/// </remarks>
internal static class DefinitionTransferEndpoints
{
    /// <summary>The longest bundle an import reads.</summary>
    internal const long MaxBundleBytes = 8L * 1024 * 1024;

    /// <summary>Maps the definition transfer endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/definitions/export", ExportAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.AgentsRead)
            .WithName("TraconExportDefinitions")
            .WithTags("Tracon", "Definitions")
            .WithSummary("Exports definitions as one tracon.definitions file.")
            .WithDescription(
                "Writes the agents, workflows, skills and MCP servers the caller's tenant keeps in the database as one " +
                "JSON file, ready for POST /api/definitions/import on another installation or tenant. With no query " +
                "parameter every definition of the four kinds is written. '?agent=' and '?workflow=' (each may repeat) " +
                "name the definitions to start from, and the file also carries what they need: the stored skills an agent " +
                "loads, the agents it calls, reviews with or shares instructions from, and the MCP servers whose tools " +
                "it uses. '?types=' (agents, workflows, skills, mcpServers; comma separated) writes every definition of " +
                "the kinds named and nothing else; naming definitions and kinds in one call is refused with 400. An " +
                "agent defined in code or in a custom source is not exported; a closure that reaches one leaves it out " +
                "and says so in 'warnings'. The file carries no tenant, no version history, no run, no session, no " +
                "secret, no OAuth token, no script permission and no plain MCP header value: an MCP server travels with " +
                "the NAMES of its headers and the names of the configuration keys its credentials are read from. " +
                "A workflow in the file needs the 'WorkflowsRead' scope as well when the call uses an API key (403 " +
                "otherwise). The export is written to the audit trail with its counts, never its content.")
            .Produces<DefinitionBundle>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapPost(
                "/api/definitions/import",
                (HttpContext httpContext, bool? dryRun, string? types, CancellationToken cancellationToken)
                    => ImportAsync(httpContext, dryRun, types, roles, cancellationToken))
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.AgentsAdmin)
            .WithName("TraconImportDefinitions")
            .WithTags("Tracon", "Definitions")
            .WithSummary("Plans, validates and imports a tracon.definitions file.")
            .Accepts<DefinitionBundle>("application/json")
            .WithDescription(
                "'?dryRun=' DEFAULTS TO TRUE: a bare call plans and validates the file and writes nothing. '?dryRun=false' " +
                "writes, and needs the Admin role. The report names, for every item, what the import does: 'create' " +
                "(the target has no such definition), 'newVersion' (an agent or workflow with other content; the " +
                "history grows by one version), 'update' (a skill or MCP server with other content, overwritten in " +
                "place), 'unchanged' (the target already holds the same content; nothing is written, so importing the " +
                "same file twice is safe), 'rejected' (a write rule refuses it), 'blocked' (it waits for an earlier step) " +
                "or 'skipped' (its kind is not in '?types='). 'changedPaths' lists the JSON paths that differ, and " +
                "'current' and 'incoming' hold the two definitions side by side. Validation is complete before the first " +
                "write: every item passes the same rules the save endpoints apply, with the agents and skills of the file " +
                "counted as already saved, so an agent may call, pin, review with or share instructions from another " +
                "agent of the same file. When any item is refused, nothing is written: a dry run answers 200 with " +
                "'valid: false', and '?dryRun=false' answers 400 with the same report. A version pin in the file names the " +
                "version the SOURCE installation gave an agent; the import translates it to the version the target holds " +
                "and refuses a pin to a version the file does not hold ('import_pin_untranslatable'). A tool of an MCP " +
                "server that the file would create or change cannot be checked before the server is saved, so an agent " +
                "that uses one is 'blocked': import the servers first with '?types=mcpServers', authorize OAuth if a " +
                "server uses it, then import the rest. A file may carry no tenant, and a member this version does not " +
                "know is refused with 400 that names its path, as is a 'formatVersion' above 1. The stores share no " +
                "transaction: if one fails after items were written, the answer is 200 with 'applied: false' and " +
                "'partial: true', 'written' lists what is in the target, and running the import again writes the rest. " +
                "Each kind needs its scope when the call uses an API key: 'AgentsAdmin' for agents, skills and MCP " +
                "servers, 'WorkflowsAdmin' for workflows; 'requiresApproval: false' on an MCP server also needs " +
                "'SecurityAdmin', as it does when saved directly. A file over 8 MiB is refused with 413. There is no lock: " +
                "two imports, or an import and a save, that run together are both written and the last write wins, " +
                "with the version history of each side intact. A dry run writes nothing, not even an audit line; an applied " +
                "import writes one summary line (counts and the SHA-256 of the file) beside the line each store writes.")
            .Produces<DefinitionImportReport>(StatusCodes.Status200OK)

            // 400 is a ProblemDetails in the document: a malformed file is the common case, and a typed client that
            // read every 400 as a report could not read it. The one 400 that carries a report ('dryRun=false' on a file
            // with a refused item) is described in the text above; a dry run returns the same report with 200.
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
    }

    private static async Task<Results<Ok<DefinitionBundle>, ProblemHttpResult>> ExportAsync(
        HttpContext httpContext,
        [FromQuery(Name = "agent")] string[]? agent,
        [FromQuery(Name = "workflow")] string[]? workflow,
        string? types,
        [FromServices] IAgentDefinitionStore agents,
        [FromServices] IWorkflowDefinitionStore workflows,
        [FromServices] IAgentSkillStore skills,
        [FromServices] IMcpServerStore servers,
        [FromServices] ITenantContext tenants,
        [FromServices] TimeProvider? timeProvider,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] TraconMetrics metrics,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var agentNames = Names(agent);
        var workflowNames = Names(workflow);

        if (ParseTypes(types, out var kinds) is { } badTypes)
        {
            return badTypes;
        }

        if ((agentNames.Count > 0 || workflowNames.Count > 0) && kinds is not null)
        {
            return Invalid(
                "Selection ambiguous",
                "Name definitions ('agent', 'workflow') or name kinds ('types'), not both: a selection by name brings what " +
                "the named definitions need, and a selection by kind brings every definition of the kinds.");
        }

        DefinitionBundle bundle;

        try
        {
            bundle = await new DefinitionExporter(agents, workflows, skills, servers, tenants, timeProvider)
                .ExportAsync(new DefinitionExportSelection(agentNames, workflowNames, kinds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DefinitionNotFoundException exception)
        {
            return TypedResults.Problem(
                title: "Definition not found",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound);
        }

        // The closure may bring a kind the caller did not ask for by name, so the scope is judged on what the file
        // holds. The endpoint's own scope covers agents, skills and MCP servers.
        if (bundle.Workflows.Count > 0 && ScopeMissing(httpContext, ApiKeyScope.WorkflowsRead) is { } missing)
        {
            return missing;
        }

        await AuditRecorder.WriteAsync(
            auditLog,
            actorResolver,
            loggerFactory.CreateLogger("Tracon.DefinitionTransferEndpoints"),
            metrics,
            tenants.TenantId,
            action: "definitions.export",
            entity: "definitions",
            before: null,
            after: Summarize(bundle.Agents.Count, bundle.Workflows.Count, bundle.Skills.Count, bundle.McpServers.Count),
            cancellationToken).ConfigureAwait(false);

        httpContext.Response.Headers.ContentDisposition = "attachment; filename=\"tracon-definitions.json\"";

        return TypedResults.Ok(bundle);
    }

    private static async Task<Results<Ok<DefinitionImportReport>, JsonHttpResult<DefinitionImportReport>, ProblemHttpResult>> ImportAsync(
        HttpContext httpContext,
        bool? dryRun,
        string? types,
        TraconRolePolicies roles,
        CancellationToken cancellationToken)
    {
        var services = httpContext.RequestServices;
        var isDryRun = dryRun ?? true;

        if (ParseTypes(types, out var kinds) is { } badTypes)
        {
            return badTypes;
        }

        if (!isDryRun && await AdminRequiredAsync(httpContext, roles, services).ConfigureAwait(false) is { } notAdmin)
        {
            return notAdmin;
        }

        var (bundle, bytes, bindError) = await RequestBodyBinding
            .ReadStrictAsync<DefinitionBundle>(httpContext, MaxBundleBytes, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        if (CheckEnvelope(bundle!) is { } badEnvelope)
        {
            return badEnvelope;
        }

        bool Selected(string kind) => kinds is null || kinds.Contains(kind);

        if ((bundle!.Workflows.Count > 0 && Selected(DefinitionKinds.Workflow)) &&
            ScopeMissing(httpContext, ApiKeyScope.WorkflowsAdmin) is { } missing)
        {
            return missing;
        }

        var tenants = services.GetRequiredService<ITenantContext>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var plan = await new DefinitionImportPlanner(
                services.GetRequiredService<IAgentDefinitionStore>(),
                services.GetRequiredService<IAgentCatalog>(),
                services.GetRequiredService<AgentDefinitionValidator>(),
                services.GetRequiredService<IWorkflowDefinitionStore>(),
                services.GetService<IWorkflowFunctionCatalog>(),
                services.GetService<IWorkflowRunTimeoutPolicy>(),
                services.GetRequiredService<IAgentSkillStore>(),
                services.GetRequiredService<AgentSkillCatalog>(),
                services.GetRequiredService<IOptions<TraconOptions>>().Value,
                services.GetRequiredService<IMcpServerStore>(),
                services.GetRequiredService<IOptionsMonitor<TraconEgressOptions>>().CurrentValue,
                services.GetRequiredService<IOptionsMonitor<TraconMcpSecurityOptions>>().CurrentValue,
                tenants,
                httpContext)
            .PlanAsync(bundle, kinds, cancellationToken)
            .ConfigureAwait(false);

        if (isDryRun)
        {
            return TypedResults.Ok(DefinitionImporter.Describe(plan, dryRun: true));
        }

        if (!plan.Valid)
        {
            return TypedResults.Json(
                DefinitionImporter.Describe(plan, dryRun: false),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var logger = loggerFactory.CreateLogger("Tracon.DefinitionTransferEndpoints");
        var importer = new DefinitionImporter(
            services.GetRequiredService<IAgentDefinitionStore>(),
            services.GetRequiredService<IWorkflowDefinitionStore>(),
            services.GetRequiredService<IAgentSkillStore>(),
            services.GetRequiredService<IMcpServerStore>(),
            tenants,
            logger);

        // The request's own cancellation does not reach the stores once the writing starts (see DefinitionImporter).
        var shutdown = services.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None;
        var report = await importer.ApplyAsync(plan, shutdown).ConfigureAwait(false);

        if (report.Written.Count > 0)
        {
            await AuditRecorder.WriteAsync(
                services.GetRequiredService<IAuditLog>(),
                services.GetRequiredService<IAuditActorResolver>(),
                logger,
                services.GetRequiredService<TraconMetrics>(),
                tenants.TenantId,
                action: "definitions.import",
                entity: "definitions",
                before: null,
                after: SummarizeImport(report, bytes!),
                shutdown).ConfigureAwait(false);
        }

        return TypedResults.Ok(report);
    }

    private static List<string> Names(string[]? values)
        => [.. (values ?? []).Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)];

    private static ProblemHttpResult? ParseTypes(string? types, out IReadOnlySet<string>? kinds)
    {
        kinds = null;

        if (string.IsNullOrWhiteSpace(types))
        {
            return null;
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kind = token.ToLowerInvariant() switch
            {
                "agents" => DefinitionKinds.Agent,
                "workflows" => DefinitionKinds.Workflow,
                "skills" => DefinitionKinds.Skill,
                "mcpservers" or "mcp-servers" => DefinitionKinds.McpServer,
                _ => null,
            };

            if (kind is null)
            {
                return Invalid(
                    "Unknown type",
                    $"'{token}' is not a kind of definition. 'types' takes agents, workflows, skills and mcpServers, comma separated.");
            }

            selected.Add(kind);
        }

        kinds = selected;

        return null;
    }

    private static ProblemHttpResult? CheckEnvelope(DefinitionBundle bundle)
    {
        if (!string.Equals(bundle.Format, DefinitionBundleFormat.Name, StringComparison.Ordinal))
        {
            return Invalid(
                "Not a definitions file",
                $"'format' must be '{DefinitionBundleFormat.Name}'; the file says '{bundle.Format}'.");
        }

        if (bundle.FormatVersion > DefinitionBundleFormat.CurrentVersion)
        {
            return Invalid(
                "File format too new",
                $"The file is 'formatVersion' {bundle.FormatVersion}; this installation reads version " +
                $"{DefinitionBundleFormat.CurrentVersion}. Upgrade this installation, or export the file again from one that " +
                "writes an older format.");
        }

        if (bundle.FormatVersion < DefinitionBundleFormat.CurrentVersion)
        {
            return Invalid("File format invalid", "'formatVersion' must be 1 or more.");
        }

        return null;
    }

    private static async ValueTask<ProblemHttpResult?> AdminRequiredAsync(
        HttpContext httpContext,
        TraconRolePolicies roles,
        IServiceProvider services)
    {
        if (roles.Admin is not { } adminPolicy || services.GetService<IAuthorizationService>() is not { } authorization)
        {
            return null;
        }

        var authorized = await authorization
            .AuthorizeAsync(httpContext.User, resource: null, adminPolicy)
            .ConfigureAwait(false);

        return authorized.Succeeded
            ? null
            : TypedResults.Problem(
                title: "Insufficient permission",
                detail: "A dry run needs the Operator role; writing the definitions ('dryRun=false') needs the Admin role.",
                statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>Refuses a call whose API key does not carry <paramref name="scope"/>.</summary>
    /// <remarks>A caller that presented no API key is governed by the role policy alone, as for every other scope.</remarks>
    private static ProblemHttpResult? ScopeMissing(HttpContext httpContext, ApiKeyScope scope)
        => ApiKeyRequestContext.Get(httpContext) is { } key && !key.Scopes.Contains(scope)
            ? TypedResults.Problem(
                title: "Insufficient scope",
                detail: $"This file holds workflows, which need the '{scope}' scope; the key does not carry it.",
                statusCode: StatusCodes.Status403Forbidden)
            : null;

    private static ProblemHttpResult Invalid(string title, string detail)
        => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static string Summarize(int agents, int workflows, int skills, int mcpServers)
        => JsonSerializer.Serialize(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["agents"] = agents,
            ["workflows"] = workflows,
            ["skills"] = skills,
            ["mcpServers"] = mcpServers,
        });

    private static string SummarizeImport(DefinitionImportReport report, byte[] body)
    {
        int Count(string type) => report.Items.Count(item => string.Equals(item.Type, type, StringComparison.Ordinal) && report.Written.Contains($"{item.Type}:{item.Name}", StringComparer.Ordinal));

        return JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sha256"] = Convert.ToHexString(SHA256.HashData(body)),
            ["agents"] = Count(DefinitionKinds.Agent),
            ["workflows"] = Count(DefinitionKinds.Workflow),
            ["skills"] = Count(DefinitionKinds.Skill),
            ["mcpServers"] = Count(DefinitionKinds.McpServer),
            ["partial"] = report.Partial,
        });
    }
}
