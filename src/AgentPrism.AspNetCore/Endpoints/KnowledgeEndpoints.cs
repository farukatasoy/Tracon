using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Knowledge base management: document upload, listing, deletion, and semantic search (Phase 51).
/// </summary>
/// <remarks>
/// Document management is an <strong>administrative</strong> operation, not the agent's own job
/// (see <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>, 51.6). While <see cref="KnowledgeIngestionService.IsSupported"/>
/// is <see langword="false"/>, every endpoint returns <c>501</c>; it does NOT silently return an empty result.
/// </remarks>
internal static class KnowledgeEndpoints
{
    /// <summary>Maps the knowledge base endpoints.</summary>
    /// <param name="builder">The endpoint group.</param>
    /// <param name="roles">The resolved role policies.</param>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapPost("/api/knowledge/{collection}/documents", UploadAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeAdmin)
            .WithName("AgentPrismUploadKnowledgeDocument")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Uploads a document to the knowledge base.")
            .Accepts<UploadDocumentRequest>("application/json")
            .WithDescription(
                "The body carries either 'text' (the server chunks and embeds it) or 'chunks' " +
                "(pre-chunked). PostgreSQL only: UsePostgreSql() and an IEmbeddingGenerator must be registered.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapGet("/api/knowledge/{collection}/documents", ListAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeRead)
            .WithName("AgentPrismListKnowledgeDocuments")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Lists the sources in a collection.")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapDelete("/api/knowledge/{collection}/documents/{sourceId}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeAdmin)
            .WithName("AgentPrismDeleteKnowledgeDocument")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Deletes all chunks of a source.")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapPost("/api/knowledge/{collection}/search", SearchAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeRead)
            .WithName("AgentPrismSearchKnowledge")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Performs a semantic search in a collection (for diagnostics and calibration).")
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
        => TypedResults.Problem(title: "Invalid request", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Strips the <c>" (Parameter 'x')"</c> suffix that <see cref="ArgumentException.Message"/>
    /// automatically appends when <see cref="ArgumentException.ParamName"/> is set — so the
    /// internal .NET parameter name does not leak into the external API contract.
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
            title: "Knowledge base not supported",
            detail: "An IVectorSearchStore (today only PostgreSQL: UsePostgreSql()) AND an " +
                    "IEmbeddingGenerator<string, Embedding<float>> must both be registered.",
            statusCode: StatusCodes.Status501NotImplemented);
}
