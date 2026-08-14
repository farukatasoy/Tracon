using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Bilgi tabani yonetimi: belge yukleme, listeleme, silme ve anlamsal arama (Faz 51).
/// </summary>
/// <remarks>
/// Belge yonetimi bir <strong>yonetim</strong> islemidir, agent'in kendi isi degildir
/// (bkz. <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>, 51.6). <see cref="KnowledgeIngestionService.IsSupported"/>
/// <see langword="false"/> iken her uc <c>501</c> doner; sessizce bos sonuc DONMEZ.
/// </remarks>
internal static class KnowledgeEndpoints
{
    /// <summary>Bilgi tabani uclarini baglar.</summary>
    /// <param name="builder">Uc grubu.</param>
    /// <param name="roles">Cozulmus rol policy'leri.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapPost("/api/knowledge/{collection}/documents", UploadAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeAdmin)
            .WithName("AgentPrismUploadKnowledgeDocument")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Bir belgeyi bilgi tabanina yukler.")
            .Accepts<UploadDocumentRequest>("application/json")
            .WithDescription(
                "Govde ya 'text' (sunucu parcalar ve gomuler) ya 'chunks' (hazir parcalar) tasir. " +
                "Yalniz PostgreSQL: UsePostgreSql() ve bir IEmbeddingGenerator kayitli olmalidir.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapGet("/api/knowledge/{collection}/documents", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeRead)
            .WithName("AgentPrismListKnowledgeDocuments")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Bir koleksiyondaki kaynaklari listeler.")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapDelete("/api/knowledge/{collection}/documents/{sourceId}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeAdmin)
            .WithName("AgentPrismDeleteKnowledgeDocument")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Bir kaynagin tum parcalarini siler.")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapPost("/api/knowledge/{collection}/search", SearchAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeRead)
            .WithName("AgentPrismSearchKnowledge")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Bir koleksiyonda anlamsal arama yapar (teshis ve kalibrasyon icin).")
            .Accepts<SearchKnowledgeRequest>("application/json")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
    }

    private static async Task<Results<Ok<UploadDocumentResponse>, ProblemHttpResult>> UploadAsync(
        string collection,
        HttpContext httpContext,
        KnowledgeIngestionService service,
        CancellationToken cancellationToken)
    {
        if (!service.IsSupported)
        {
            return NotSupported();
        }

        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<UploadDocumentRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        try
        {
            var chunks = request.Chunks?
                .Select(static chunk => new VectorChunk
                {
                    Index = chunk.Index,
                    Content = chunk.Content,
                    Embedding = chunk.Embedding ?? [],
                    Metadata = chunk.Metadata,
                })
                .ToArray();

            var count = await service
                .IngestAsync(collection, request.SourceId, request.Text, chunks, cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Ok(new UploadDocumentResponse(request.SourceId, count));
        }
        catch (ArgumentException ex)
        {
            return Invalid(CleanMessage(ex));
        }
    }

    private static async Task<Results<Ok<IReadOnlyList<string>>, ProblemHttpResult>> ListAsync(
        string collection,
        KnowledgeIngestionService service,
        CancellationToken cancellationToken)
    {
        if (!service.IsSupported)
        {
            return NotSupported();
        }

        var sources = await service.ListSourcesAsync(collection, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(sources);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        string collection,
        string sourceId,
        KnowledgeIngestionService service,
        CancellationToken cancellationToken)
    {
        if (!service.IsSupported)
        {
            return NotSupported();
        }

        await service.DeleteSourceAsync(collection, sourceId, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<SearchKnowledgeHit>>, ProblemHttpResult>> SearchAsync(
        string collection,
        HttpContext httpContext,
        KnowledgeIngestionService service,
        CancellationToken cancellationToken)
    {
        if (!service.IsSupported)
        {
            return NotSupported();
        }

        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<SearchKnowledgeRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        try
        {
            var hits = await service
                .SearchAsync(collection, request.Query, request.Top, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<SearchKnowledgeHit> results = hits
                .Select(static hit => new SearchKnowledgeHit
                {
                    SourceId = hit.SourceId,
                    ChunkIndex = hit.ChunkIndex,
                    Content = hit.Content,
                    Distance = hit.Distance,
                    Metadata = hit.Metadata,
                })
                .ToArray();

            return TypedResults.Ok(results);
        }
        catch (ArgumentException ex)
        {
            return Invalid(CleanMessage(ex));
        }
    }

    private static ProblemHttpResult Invalid(string detail)
        => TypedResults.Problem(title: "Gecersiz istek", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// <see cref="ArgumentException.Message"/>'in <see cref="ArgumentException.ParamName"/>
    /// doluyken otomatik ekledigi <c>" (Parameter 'x')"</c> sonekini atar — ic
    /// .NET parametre adi dis API sozlesmesine sizmasin diye.
    /// </summary>
    private static string CleanMessage(ArgumentException ex)
    {
        if (ex.ParamName is not { Length: > 0 } paramName)
        {
            return ex.Message;
        }

        var suffix = $" (Parameter '{paramName}')";

        return ex.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? ex.Message[..^suffix.Length]
            : ex.Message;
    }

    private static ProblemHttpResult NotSupported()
        => TypedResults.Problem(
            title: "Bilgi tabani desteklenmiyor",
            detail: "Bir IVectorSearchStore (bugun yalniz PostgreSQL: UsePostgreSql()) VE bir " +
                    "IEmbeddingGenerator<string, Embedding<float>> birlikte kayitli olmalidir.",
            statusCode: StatusCodes.Status501NotImplemented);
}
