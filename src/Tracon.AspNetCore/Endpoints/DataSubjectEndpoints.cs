using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Data subject export and erasure endpoints (GDPR-style "right to erasure").</summary>
/// <remarks>
/// <para>
/// Both endpoints need <see cref="IDataSubjectResolver"/> to be registered; there
/// is no default implementation (Tracon does not store personal identity —
/// see the interface's remarks). Without one, both return <c>409</c> — never a
/// silent empty result that could be read as "already erased".
/// </para>
/// <para>
/// <c>dryRun</c> defaults to <see langword="true"/> for the delete endpoint: an
/// irreversible operation is not the default behavior of a bare call.
/// </para>
/// </remarks>
internal static class DataSubjectEndpoints
{
    /// <summary>The response header an export carries the count of conversations it could not look up in.</summary>
    internal const string UnresolvedConversationsHeader = "X-Tracon-Unresolved-Conversations";

    /// <summary>Maps the data subject endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapGet("/api/data-subjects/{id}/export", ExportAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconExportDataSubject")
            .WithTags("Tracon", "Governance")
            .WithSummary("Exports a data subject's content as one JSON document.")
            .WithDescription(
                "'{id}' is resolved through IDataSubjectResolver, which is registered by the " +
                "consumer — Tracon does not store personal identity. Without a registered " +
                "resolver this returns 409, never an empty document. The document holds every " +
                "column of every matching row, keyed by target table (session state, runs, run " +
                "inputs, attachment METADATA only — no file bytes, voice session summaries, run " +
                "scores, workflow checkpoints, conversations, conversation items, and responses); " +
                "a target with no matching rows is present as an empty array, not omitted. A " +
                "session's internal conversation is found by restoring the session through its own " +
                "agent. An agent that is no longer registered cannot do that: the session's row is " +
                "still in the document, its conversation is not, and the response header " +
                "'X-Tracon-Unresolved-Conversations' says how many sessions this applies to (an " +
                "upper bound; '0' when every conversation was looked up; written on every 200 " +
                "response, and a proxy may drop it). A session whose saved state cannot be restored " +
                "is counted the same way. An agent that is registered but cannot be compiled " +
                "(a tenant key with no value, an egress policy that no longer allows the provider) " +
                "fails the whole call with 400 and nothing is read.")
            // Declaring any response removes the default 200 from the document; this keeps the one the endpoint had.
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        builder.MapDelete("/api/data-subjects/{id}", EraseAsync)
            .RequireRole(roles.Admin)
            .RequireApiKeyScope(ApiKeyScope.SecurityAdmin)
            .WithName("TraconEraseDataSubject")
            .WithTags("Tracon", "Governance")
            .WithSummary("Deletes a data subject's content.")
            .WithDescription(
                "'{id}' is resolved through IDataSubjectResolver; without one this returns 409, " +
                "never a silent no-op that could be read as 'already erased'. '?dryRun=' " +
                "DEFAULTS TO TRUE: a bare call previews the row counts and deletes nothing; " +
                "'?dryRun=false' deletes for real. The audit trail (audit_log) is never touched " +
                "— it is deliberately outside a data subject's erasable content (by design: " +
                "an audit record is 'who did what', not the subject's own " +
                "data) — but the erasure ITSELF is written there, with the row count per " +
                "target; if that write fails, every delete is rolled back and this call fails, " +
                "the same rule Approval decisions follow. A run of the subject that is still " +
                "Running or Queued would write its session and checkpoints back after the erasure " +
                "commits: '?dryRun=false' is refused with 409 that names those runs, and the preview " +
                "lists them in 'activeRunIds'. '?force=true' erases anyway, for a row a crashed " +
                "process left behind; cancel a run that is really running first. A session's " +
                "internal conversation is found by restoring the session through its own agent. " +
                "An agent that is no longer registered cannot do that: the session row IS erased, " +
                "its conversation stays, and 'unresolvedConversations' says how many sessions this " +
                "applies to (an upper bound, in the preview as well as in the erasure, and in the " +
                "audit entry; a session whose saved state cannot be restored is counted too). " +
                "Preview first: when it is above 0 because an agent was deleted or renamed, register " +
                "the agent again and erase afterwards, and the conversation goes with the session. " +
                "A repeated call finds the session already gone and counts 0, so keep the answer of " +
                "the first call. An agent that is registered but cannot be compiled (a tenant key " +
                "with no value, an egress policy that no longer allows the provider) fails the " +
                "whole call with 400, and nothing is erased.")
            .Produces<DataSubjectErasureResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> ExportAsync(
        string id,
        [FromServices] IDataSubjectResolver? resolver,
        [FromServices] IDataSubjectStore store,
        [FromServices] SessionConversationResolver conversations,
        [FromServices] ITenantContext tenants,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (resolver is null)
        {
            return ResolverNotRegistered();
        }

        var (scope, unresolved, refusal) = await ExpandScopeAsync(resolver, conversations, id, tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        var export = await store.ExportAsync(tenants.TenantId, scope!, cancellationToken).ConfigureAwait(false);

        // The document is the store's and is written as it is; the count is not part of it, so it travels beside it.
        context.Response.Headers[UnresolvedConversationsHeader] = unresolved.ToString(CultureInfo.InvariantCulture);

        return TypedResults.Content(export.Json, "application/json");
    }

    private static async Task<Results<Ok<DataSubjectErasureResult>, ProblemHttpResult>> EraseAsync(
        string id,
        bool? dryRun,
        bool? force,
        [FromServices] IDataSubjectResolver? resolver,
        [FromServices] IDataSubjectStore store,
        [FromServices] IRunStore runs,
        [FromServices] SessionConversationResolver conversations,
        [FromServices] ITenantContext tenants,
        [FromServices] IAuditLog auditLog,
        [FromServices] IAuditActorResolver actorResolver,
        [FromServices] TimeProvider? timeProvider,
        [FromServices] TraconMetrics metrics,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (resolver is null)
        {
            return ResolverNotRegistered();
        }

        var (expanded, unresolved, refusal) = await ExpandScopeAsync(resolver, conversations, id, tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (refusal is not null)
        {
            return refusal;
        }

        var scope = expanded!;
        var isDryRun = dryRun ?? true;

        // A run still in progress writes its session and its checkpoints
        // back after the erasure commits.
        var activeRunIds = await FindActiveRunsAsync(runs, tenants.TenantId, scope, cancellationToken).ConfigureAwait(false);

        if (isDryRun)
        {
            var preview = await store.PreviewAsync(tenants.TenantId, scope, cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok(new DataSubjectErasureResult
            {
                DryRun = true,
                RowsByTarget = preview,
                ActiveRunIds = activeRunIds,
                UnresolvedConversations = unresolved,
            });
        }

        if (activeRunIds.Count > 0 && force != true)
        {
            return TypedResults.Problem(
                title: "Data subject has runs in progress",
                detail: $"{activeRunIds.Count} run(s) of the subject are still Running or Queued: " +
                        $"{string.Join(", ", activeRunIds)}. A run in progress writes its session and " +
                        "checkpoints back after the erasure commits. Cancel the runs or wait for them, then " +
                        "erase; '?force=true' erases anyway (for a row a crashed process left behind).",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["activeRunIds"] = activeRunIds });
        }

        var actor = actorResolver.Resolve();
        var logger = loggerFactory.CreateLogger("Tracon.DataSubjectEndpoints");

        // The audit entry is written BEFORE the erasure commits, from
        // inside IDataSubjectStore.EraseAsync's transaction. If the write fails,
        // every delete is rolled back and the exception propagates — an erasure
        // that cannot be written to the audit trail is not applied.
        //
        // The fail-closed write is therefore called from INSIDE the callback, not
        // around it: the store, not this endpoint, decides when the row runs, and
        // moving the call outside would commit the deletes first.
        var deleted = await store.EraseAsync(
            tenants.TenantId,
            scope,
            (counts, ct) => AuditRecorder.WriteOrThrowAsync(
                auditLog,
                actor,
                logger,
                metrics,
                tenants.TenantId,
                action: "data_subject.erase",
                entity: $"data-subject:{id}",
                before: null,
                // Redaction applies here too. Today's payload carries no secret,
                // but this was the ONLY direct IAuditLog write that skipped the
                // filter; the next change to the payload's shape would have had
                // no protection at all.
                after: DescribeErasure(id, counts, unresolved),
                refusal: $"The erasure of data subject '{id}' was not applied",
                timeProvider,
                ct),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new DataSubjectErasureResult
        {
            DryRun = false,
            RowsByTarget = deleted,
            ActiveRunIds = activeRunIds,
            UnresolvedConversations = unresolved,
        });
    }

    private static readonly RunStatus[] InProgress = [RunStatus.Running, RunStatus.Queued];

    /// <summary>The subject's runs that are still <c>Running</c> or <c>Queued</c>, in this tenant.</summary>
    /// <remarks>
    /// The runs the resolver names and the runs of the sessions it names. A child run
    /// that carries no session is covered by its root, which is in progress while the
    /// child is.
    /// </remarks>
    private static async ValueTask<IReadOnlyList<Guid>> FindActiveRunsAsync(
        IRunStore runs,
        string tenantId,
        DataSubjectScope scope,
        CancellationToken cancellationToken)
    {
        var active = new HashSet<Guid>();

        foreach (var runId in scope.RunIds)
        {
            if (await runs.GetRunAsync(runId, cancellationToken).ConfigureAwait(false) is { } run
                && string.Equals(run.TenantId, tenantId, StringComparison.Ordinal)
                && run.Status is RunStatus.Running or RunStatus.Queued)
            {
                active.Add(run.Id);
            }
        }

        foreach (var sessionId in scope.SessionIds)
        {
            foreach (var status in InProgress)
            {
                var found = await runs
                    .QueryRunsAsync(
                        new RunQuery { SessionId = sessionId, Status = status, TenantId = tenantId, OnlyRootRuns = false, Take = 200 },
                        cancellationToken)
                    .ConfigureAwait(false);

                active.UnionWith(found.Select(static run => run.Id));
            }
        }

        return [.. active.Order()];
    }

    /// <summary>
    /// Resolves a subject's scope and folds in the internal chat-history
    /// conversation of every resolved session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A resolver only ever names sessions/runs/conversations in ITS OWN
    /// identity system; the conversation id an ordinary agent session's chat
    /// history is stored under is Tracon's own internal bookkeeping (a
    /// session's state bag, which is not something a resolver author is expected to
    /// know how to read). Without this step, <see cref="DataSubjectScope.SessionIds"/>
    /// alone could erase a session's ROW but never its conversation history —
    /// see <see cref="SessionConversationResolver"/>.
    /// </para>
    /// <para>
    /// Reading that history restores the session through its agent, and an agent the catalog refuses to compile (a tenant
    /// provider binding whose key has no value, an egress policy that no longer allows the provider) is the same
    /// <c>400 Agent compilation failed</c> a run of that agent gets. It is an error and not a skip: an erasure that skipped
    /// the session would delete the rest of the subject's rows, leave its conversation behind and answer <c>200</c>. Only
    /// the catalog's refusal is answered here; the resolver is the consumer's code and its own failures are not.
    /// </para>
    /// <para>
    /// An agent the catalog does not know at all (deleted, renamed) is the other case. The session is skipped and
    /// COUNTED: the count is the second element of the result, and the callers put it in the answer and, for an erasure,
    /// in the audit entry. A refusal discards the count with the rest of the lookup: no answer, no audit entry and no
    /// delete follows it, in whatever order the sessions came.
    /// </para>
    /// </remarks>
    private static async ValueTask<(DataSubjectScope? Scope, int Unresolved, ProblemHttpResult? Refusal)> ExpandScopeAsync(
        IDataSubjectResolver resolver,
        SessionConversationResolver conversations,
        string subjectId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var scope = await resolver.ResolveAsync(subjectId, tenantId, cancellationToken).ConfigureAwait(false);

        SessionConversationLookup lookup;

        try
        {
            lookup = await conversations
                .ResolveAsync(scope.SessionIds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TraconException ex)
        {
            return (null, 0, TypedResults.Problem(
                title: "Agent compilation failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest));
        }

        if (lookup.ConversationIds.Count == 0)
        {
            return (scope, lookup.Unresolved, null);
        }

        return (
            scope with
            {
                ConversationIds = scope.ConversationIds
                    .Concat(lookup.ConversationIds)
                    .Distinct()
                    .ToArray(),
            },
            lookup.Unresolved,
            null);
    }

    /// <summary>Summarizes an erasure for the audit trail.</summary>
    private static string DescribeErasure(string subjectId, IReadOnlyDictionary<string, int> rowsByTarget, int unresolvedConversations)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteString("subjectId", subjectId);
        writer.WriteStartObject("rowsByTarget");

        foreach (var (target, count) in rowsByTarget)
        {
            writer.WriteNumber(target, count);
        }

        writer.WriteEndObject();
        writer.WriteNumber("unresolvedConversations", unresolvedConversations);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static ProblemHttpResult ResolverNotRegistered()
        => TypedResults.Problem(
            title: "No data subject resolver registered",
            detail: "IDataSubjectResolver is not registered. Tracon does not store personal " +
                     "identity and cannot resolve a subject id to sessions/runs/conversations on " +
                     "its own — register an IDataSubjectResolver implementation to use this endpoint.",
            statusCode: StatusCodes.Status409Conflict);
}
