using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Endpoints for uploading, downloading, listing, and deleting attachments.
/// </summary>
/// <remarks>
/// Binary content lives in the <c>attachments</c> table; only a small
/// reference travels with messages. Rationale: <c>docs/14-COK-MODLULUK.md</c>, sections 14.1 and 14.4.
/// </remarks>
internal static class AttachmentEndpoints
{
    /// <summary>Maps the attachment endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapPost("/api/attachments", UploadAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismUploadAttachment")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Uploads a new attachment.")
            .WithDescription(
                "The body must be 'multipart/form-data' and must carry a 'file' field. " +
                "The type is validated by magic bytes, not by the Content-Type the client reports.")
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
            .WithName("AgentPrismDownloadAttachment")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Streams the raw content of an attachment.")
            // Binary body; the actual type comes from the attachment's own
            // MediaType field and cannot be known at compile time.
            .Produces<Stream>(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapGet("/api/attachments", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListAttachments")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Lists attachments.");

        builder.MapDelete("/api/attachments/{id:guid}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDeleteAttachment")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Deletes an attachment.");
    }

    private static async Task<Results<Created<AttachmentDescriptor>, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        [FromQuery] string? sessionId,
        HttpContext httpContext,
        IAttachmentStore store,
        AttachmentTypeGuard guard,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken)
    {
        var descriptor = await store.GetAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false);

        if (descriptor is null)
        {
            return NotFound(id);
        }

        var content = await store.OpenReadAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false);

        return content is null ? NotFound(id) : new AttachmentDownloadResult(descriptor, content);
    }

    private static async Task<Ok<IReadOnlyList<AttachmentDescriptor>>> ListAsync(
        [FromQuery] string? sessionId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        IAttachmentStore store,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken)
        => await store.DeleteAsync(tenantContext.TenantId, id, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : NotFound(id);

    private static ProblemHttpResult Invalid(string title, string detail)
        => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult NotFound(Guid id)
        => TypedResults.Problem(
            title: "Attachment not found",
            detail: $"There is no attachment with id '{id}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// Result that streams attachment content with safe headers.
    /// </summary>
    /// <remarks>
    /// <c>X-Content-Type-Options: nosniff</c> and <c>Content-Disposition: attachment</c>
    /// are applied together: the browser never interprets and executes the
    /// content inline (for example as HTML). Rationale: <c>docs/14-COK-MODLULUK.md</c>, section 14.4.
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
