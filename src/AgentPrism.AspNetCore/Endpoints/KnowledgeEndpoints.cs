using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AgentPrism;

/// <summary>
/// Knowledge base management: document upload, listing, deletion, and semantic search.
/// </summary>
/// <remarks>
/// Document management is an <strong>administrative</strong> operation, not the agent's own job.
/// While <see cref="KnowledgeIngestionService.IsSupported"/>
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
            .WithDescription(
                "The response is a flat list of source identifiers, not the chunks or their " +
                "text: a source is the unit a document was uploaded and is deleted as. An " +
                "unknown collection is not an error — it simply has no sources and returns an " +
                "empty list. Knowledge storage requires PostgreSQL; without it the response " +
                "is 501.")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapDelete("/api/knowledge/{collection}/documents/{sourceId}", DeleteAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeAdmin)
            .WithName("AgentPrismDeleteKnowledgeDocument")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Deletes all chunks of a source.")
            .WithDescription(
                "Every chunk and embedding produced from the source is removed; re-uploading " +
                "the document is the only way back, and it costs a fresh round of embedding " +
                "calls. The call is idempotent: an unknown source id still answers 204, because " +
                "the requested end state — no such source — already holds. Knowledge storage " +
                "requires PostgreSQL; without it the response is 501.")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        builder.MapPost("/api/knowledge/{collection}/search", SearchAsync)
            .RequireRole(roles.Reader)
            .RequireApiKeyScope(ApiKeyScope.KnowledgeRead)
            .WithName("AgentPrismSearchKnowledge")
            .WithTags("AgentPrism", "Knowledge")
            .WithSummary("Performs a semantic search in a collection (for diagnostics and calibration).")
            .WithDescription(
                "This runs the same retrieval an agent performs, so it is how a retrieval " +
                "problem is separated from a prompt problem: if the right chunk does not come " +
                "back here, the agent was never going to see it. The query is embedded, which " +
                "costs one embedding call per request. Each hit carries its distance — smaller " +
                "is closer — along with the chunk text and its metadata, so a relevance " +
                "threshold can be calibrated from real values. Knowledge storage requires " +
                "PostgreSQL; without it the response is 501.")
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
