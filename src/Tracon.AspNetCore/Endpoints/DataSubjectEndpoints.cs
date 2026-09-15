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
                "scores, conversations, conversation items, and responses); a target with no " +
                "matching rows is present as an empty array, not omitted.");

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
                "the same rule Approval decisions follow.");
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> ExportAsync(
        string id,
        [FromServices] IDataSubjectResolver? resolver,
        [FromServices] IDataSubjectStore store,
        [FromServices] SessionConversationResolver conversations,
        [FromServices] ITenantContext tenants,
        CancellationToken cancellationToken)
    {
        if (resolver is null)
        {
            return ResolverNotRegistered();
        }

        var scope = await ExpandScopeAsync(resolver, conversations, id, tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);
        var export = await store.ExportAsync(tenants.TenantId, scope, cancellationToken).ConfigureAwait(false);

        return TypedResults.Content(export.Json, "application/json");
    }

    private static async Task<Results<Ok<DataSubjectErasureResult>, ProblemHttpResult>> EraseAsync(
        string id,
        bool? dryRun,
        [FromServices] IDataSubjectResolver? resolver,
        [FromServices] IDataSubjectStore store,
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

        var scope = await ExpandScopeAsync(resolver, conversations, id, tenants.TenantId, cancellationToken)
            .ConfigureAwait(false);
        var isDryRun = dryRun ?? true;

        if (isDryRun)
        {
            var preview = await store.PreviewAsync(tenants.TenantId, scope, cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok(new DataSubjectErasureResult { DryRun = true, RowsByTarget = preview });
        }

        var actor = actorResolver.Resolve();
        var logger = loggerFactory.CreateLogger("Tracon.DataSubjectEndpoints");

        // 🚨 K-370: the audit entry is written BEFORE the erasure commits, from
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
                after: DescribeErasure(id, counts),
                refusal: $"The erasure of data subject '{id}' was not applied",
                timeProvider,
                ct),
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(new DataSubjectErasureResult { DryRun = false, RowsByTarget = deleted });
    }

    /// <summary>
    /// Resolves a subject's scope and folds in the internal chat-history
    /// conversation of every resolved session.
    /// </summary>
    /// <remarks>
    /// A resolver only ever names sessions/runs/conversations in ITS OWN
    /// identity system; the conversation id an ordinary agent session's chat
    /// history is stored under is Tracon's own internal bookkeeping (a
    /// session's state bag, which is not something a resolver author is expected to
    /// know how to read). Without this step, <see cref="DataSubjectScope.SessionIds"/>
    /// alone could erase a session's ROW but never its conversation history —
    /// see <see cref="SessionConversationResolver"/>.
    /// </remarks>
    private static async ValueTask<DataSubjectScope> ExpandScopeAsync(
        IDataSubjectResolver resolver,
        SessionConversationResolver conversations,
        string subjectId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var scope = await resolver.ResolveAsync(subjectId, tenantId, cancellationToken).ConfigureAwait(false);
        var internalConversationIds = await conversations
            .ResolveAsync(scope.SessionIds, cancellationToken)
            .ConfigureAwait(false);

        if (internalConversationIds.Count == 0)
        {
            return scope;
        }

        return scope with
        {
            ConversationIds = scope.ConversationIds
                .Concat(internalConversationIds)
                .Distinct()
                .ToArray(),
        };
    }

    /// <summary>Summarizes an erasure for the audit trail.</summary>
    private static string DescribeErasure(string subjectId, IReadOnlyDictionary<string, int> rowsByTarget)
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
