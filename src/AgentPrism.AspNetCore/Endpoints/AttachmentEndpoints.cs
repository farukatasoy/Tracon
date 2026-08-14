using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Ek yukleme, indirme, listeleme ve silme uclari.
/// </summary>
/// <remarks>
/// Ikili icerik <c>attachments</c> tablosunda yasar; mesajlarda yalniz kucuk bir
/// referans tasinir. Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1 ve 14.4.
/// </remarks>
internal static class AttachmentEndpoints
{
    /// <summary>Ek uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapPost("/api/attachments", UploadAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismUploadAttachment")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Yeni bir ek yukler.")
            .WithDescription(
                "Govde 'multipart/form-data' olmalidir ve bir 'file' alani tasimalidir. " +
                "Tur, istemcinin bildirdigi Content-Type'a degil sihirli bayta gore dogrulanir.")
            // ASP.NET Core, minimal API'de IFormFile parametresi gordugunde uca
            // OTOMATIK olarak anti-forgery gerektiren metadata ekler (CSRF
            // korumasi, tarayici form gonderimleri icin varsayilandir). Bu API
            // tarayici oturumu degil bearer token ile korunur ve uygulama
            // UseAntiforgery() cagirmaz; acikca devre disi birakilmazsa her
            // istek "middleware not found" ile 500 doner. Olculdu.
            .DisableAntiforgery();

        builder.MapGet("/api/attachments/{id:guid}", DownloadAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismDownloadAttachment")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Bir ekin ham icerigini akitir.")
            // Ikili govde; gercek turu ekin kendi MediaType alanindan gelir ve
            // derleme zamaninda bilinemez.
            .Produces<Stream>(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        builder.MapGet("/api/attachments", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.RunsRead)
            .WithName("AgentPrismListAttachments")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Ekleri listeler.");

        builder.MapDelete("/api/attachments/{id:guid}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismDeleteAttachment")
            .WithTags("AgentPrism", "Attachments")
            .WithSummary("Bir eki siler.");
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
    /// Ek icerigini guvenli basliklarla akitan sonuc.
    /// </summary>
    /// <remarks>
    /// <c>X-Content-Type-Options: nosniff</c> ve <c>Content-Disposition: attachment</c>
    /// birlikte uygulanir: tarayici icerigi asla satir ici (ornegin HTML olarak)
    /// yorumlayip calistirmaz. Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.4.
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
            // Tirnak ve satir sonu karakterleri baslik enjeksiyonunu onlemek icin
            // atilir; dosya adi yalnizca goruntuleme amaclidir.
            var safe = fileName
                .Replace("\"", string.Empty, StringComparison.Ordinal)
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);

            return $"attachment; filename=\"{safe}\"";
        }
    }
}
