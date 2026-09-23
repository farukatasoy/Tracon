using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Tracon;

/// <summary>
/// Endpoints for uploading, downloading, listing, and deleting attachments.
/// </summary>
/// <remarks>
/// Binary content lives in the <c>attachments</c> table; only a small
/// reference travels with messages.
/// </remarks>
internal static class AttachmentEndpoints
{
    /// <summary>Maps the attachment endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, TraconRolePolicies roles)
    {
        builder.MapPost("/api/attachments", UploadAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconUploadAttachment")
            .WithTags("Tracon", "Attachments")
            .WithSummary("Uploads a new attachment.")
            .WithDescription(
                "The body must be 'multipart/form-data' and must carry a 'file' field. " +
                "The type is validated by magic bytes, not by the Content-Type the client reports. " +
                "If a registered IRunAuthorizationHandler denies the caller, the response is 403.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // When ASP.NET Core sees an IFormFile parameter in a minimal API, it
            // AUTOMATICALLY adds metadata that requires anti-forgery (CSRF
            // protection, the default for browser form submissions). This API
            // is secured by a bearer token, not a browser session, and the
            // application does not call UseAntiforgery(); unless explicitly
            // disabled, every request returns 500 with "middleware not found".
            // Measured.
            .DisableAntiforgery();

        builder.MapGet("/api/attachments/{id:guid}", DownloadAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconDownloadAttachment")
            .WithTags("Tracon", "Attachments")
            .WithSummary("Streams the raw content of an attachment.")
            .WithDescription(
                "The response carries the attachment's own stored media type, an ETag holding the " +
                "content's SHA-256, and 'Content-Disposition: attachment' together with " +
                "'X-Content-Type-Options: nosniff' — a browser therefore downloads the bytes " +
                "instead of rendering them, so uploaded HTML can never execute in the console's " +
                "origin. The token travels in the Authorization header, so a browser cannot use " +
                "this URL directly as an image or audio element source; fetch the bytes and wrap " +
                "them in an object URL instead. If a registered IRunAuthorizationHandler denies the " +
                "caller, the response is 404, identical to an attachment that does not exist.")
            // Binary body; the actual type comes from the attachment's own
            // MediaType field and cannot be known at compile time.
            .Produces<Stream>(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapGet("/api/attachments", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("TraconListAttachments")
            .WithTags("Tracon", "Attachments")
            .WithSummary("Lists attachments.")
            .WithDescription(
                "Only descriptors are returned — file name, media type, size, and content hash — " +
                "never the bytes; fetch those from the download endpoint. 'sessionId' narrows the " +
                "list to one session, and attachments uploaded without a session are reachable " +
                "only without that filter. Paging is offset based: 'skip' defaults to 0, 'take' " +
                "to 50, and 'take' is clamped to 1..200 instead of being rejected. If a registered " +
                "IRunAuthorizationHandler denies the caller, the response is 403 — the list is " +
                "REJECTED, never silently filtered.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        builder.MapDelete("/api/attachments/{id:guid}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("TraconDeleteAttachment")
            .WithTags("Tracon", "Attachments")
            .WithSummary("Deletes an attachment.")
            .WithDescription(
                "The bytes are removed immediately; there is no soft delete. Messages that already " +
                "reference the attachment keep the reference and it stops resolving, so delete an " +
                "attachment only when its conversation no longer needs to be replayed. Deleting " +
                "the owning session removes its attachments as well, which is usually the call to " +
                "reach for. An unknown id returns 404, and so does a denial by a registered " +
                "IRunAuthorizationHandler.");
    }

    private static async Task<Results<Created<AttachmentDescriptor>, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        [FromQuery] string? sessionId,
        HttpContext httpContext,
        IAttachmentStore store,
        AttachmentTypeGuard guard,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        CancellationToken cancellationToken)
    {
        // 🚨 An upload addresses no existing resource, so a denial is 403, not
        // the 404 the single-attachment endpoints give: there is no identity to
        // hide behind a "not found". No run exists yet either, so RunId is null
        // and the handler decides from the tenant, the user, and the session.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenantContext,
                    runId: null,
                    agentName: null,
                    sessionId,
                    attributionContext,
                    RunAccess.Attachment,
                    NotAuthorized(),
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        if (file.Length == 0)
        {
            return Invalid("Attachment cannot be empty", "The 'file' field is empty.");
        }

        if (file.Length > guard.MaxBytes)
        {
            return Invalid(
                "Attachment too large",
                $"'{file.FileName}' is {file.Length} bytes; the limit is {guard.MaxBytes} bytes.");
        }

        byte[] data;
        var upload = file.OpenReadStream();

        await using (upload.ConfigureAwait(false))
        {
            var buffer = new MemoryStream(checked((int)file.Length));

            await using (buffer.ConfigureAwait(false))
            {
                await upload.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                data = buffer.ToArray();
            }
        }

        var validation = guard.Validate(data);

        if (!validation.IsValid)
        {
            return Invalid("Attachment type rejected", validation.Error!);
        }

        var descriptor = await store.SaveAsync(
            new AttachmentContent
            {
                TenantId = tenantContext.TenantId,
                SessionId = sessionId,
                FileName = file.FileName,
                MediaType = validation.MediaType!,
                Data = data,
                CreatedBy = actorResolver.Resolve(),
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Created($"{httpContext.Request.Path}/{descriptor.Id}", descriptor);
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        IAttachmentStore store,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var descriptor = await store.GetAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (descriptor is null)
        {
            return NotFound(id);
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenantContext,
                    descriptor.RunId,
                    agentName: null,
                    descriptor.SessionId,
                    attributionContext,
                    RunAccess.Attachment,
                    NotFound(id),
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var content = await store.OpenReadAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false);

        return content is null ? NotFound(id) : new AttachmentDownloadResult(descriptor, content);
    }

    private static async Task<Results<Ok<IReadOnlyList<AttachmentDescriptor>>, ProblemHttpResult>> ListAsync(
        [FromQuery] string? sessionId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        IAttachmentStore store,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // A denied list is REJECTED (403), never silently filtered: filtering
        // rows out server-side would break the skip/take paging contract.
        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenantContext,
                    runId: null,
                    agentName: null,
                    sessionId,
                    attributionContext,
                    RunAccess.Attachment,
                    NotAuthorized(),
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        var results = await store.ListAsync(
            new AttachmentQuery
            {
                TenantId = tenantContext.TenantId,
                SessionId = sessionId,
                Skip = Math.Max(skip ?? 0, 0),
                Take = Math.Clamp(take ?? 50, 1, 200),
            },
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(results);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        IAttachmentStore store,
        ITenantContext tenantContext,
        [FromServices] IRunAuthorizationHandler? runAuthorizationHandler,
        [FromServices] IRunAttributionContext? attributionContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // The descriptor is read first so the handler learns WHICH attachment
        // is about to go. An unknown id still ends in the same 404 it always
        // did, only one step earlier.
        var descriptor = await store.GetAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (descriptor is null)
        {
            return NotFound(id);
        }

        if (await RunAuthorizationGate
                .CheckRunResourceAsync(
                    runAuthorizationHandler,
                    tenantContext,
                    descriptor.RunId,
                    agentName: null,
                    descriptor.SessionId,
                    attributionContext,
                    RunAccess.Attachment,
                    NotFound(id),
                    httpContext,
                    cancellationToken)
                .ConfigureAwait(false) is { } authorizationProblem)
        {
            return authorizationProblem;
        }

        return await store.DeleteAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(id);
    }

    private static ProblemHttpResult Invalid(string title, string detail)
        => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(Guid id)
        => TypedResults.Problem(
            title: "Attachment not found",
            detail: $"There is no attachment with id '{id}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>The response a denied UPLOAD or LIST gets: 403, naming no attachment.</summary>
    private static ProblemHttpResult NotAuthorized()
        => TypedResults.Problem(
            title: "Run not authorized",
            detail: "The registered IRunAuthorizationHandler denied this request.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Result that streams attachment content with safe headers.
    /// </summary>
    /// <remarks>
    /// <c>X-Content-Type-Options: nosniff</c> and <c>Content-Disposition: attachment</c>
    /// are applied together: the browser never interprets and executes the
    /// content inline (for example as HTML).
    /// </remarks>
    private sealed class AttachmentDownloadResult(AttachmentDescriptor descriptor, Stream content) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var response = httpContext.Response;
            response.ContentType = descriptor.MediaType;
            response.ContentLength = descriptor.ByteSize;
            response.Headers["ETag"] = $"\"{descriptor.Sha256}\"";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Disposition"] = BuildContentDisposition(descriptor.FileName);

            await using (content.ConfigureAwait(false))
            {
                await content.CopyToAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
            }
        }

        private static string BuildContentDisposition(string fileName)
        {
            // Quote and line-break characters are stripped to prevent header
            // injection; the file name is for display purposes only.
            var safe = fileName
                .Replace("\"", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);

            return $"attachment; filename=\"{safe}\"";
        }
    }
}
