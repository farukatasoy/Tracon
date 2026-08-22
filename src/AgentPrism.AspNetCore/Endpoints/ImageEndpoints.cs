using System.Drawing;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Optional operator endpoint for image generation.</summary>
/// <remarks>
/// This endpoint is outside an agent run. It returns the observed usage to the
/// caller but never writes a <c>tool_invocations</c> row, because that table has
/// a required run id. Use <c>generate_image</c> when durable run metering is needed.
/// </remarks>
#pragma warning disable MEAI001
internal static class ImageEndpoints
{
    /// <summary>Maps the enabled image-generation endpoint.</summary>
    public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
    {
        builder.MapPost("/api/images/generate", GenerateAsync)
            .RequireRole(roles.Operator)
            .RequireApiKeyScope(ApiKeyScope.RunsWrite)
            .WithName("AgentPrismGenerateImage")
            .WithTags("AgentPrism", "Images")
            .WithSummary("Generates images and saves them as attachments.")
            .Accepts<ImageGenerationOperatorRequest>("application/json")
            .Produces<ImageGenerationOperatorResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithDescription(
                "This is an operator action outside a run. Generated images are saved as attachments and " +
                "the response returns their descriptors and configured cost. To record usage in tool_invocations, " +
                "use the agent's generate_image tool instead.");
    }

    private static async Task<Results<Ok<ImageGenerationOperatorResponse>, ProblemHttpResult>> GenerateAsync(
        HttpContext httpContext,
        ImageGeneratorResolver imageGeneratorResolver,
        IOptions<AgentPrismImageOptions> imageOptions,
        ImagePricing pricing,
        ImageAttachmentWriter attachmentWriter,
        ITenantContext tenantContext,
        IAuditActorResolver actorResolver,
        CancellationToken cancellationToken)
    {
        var (bound, bindError) = await RequestBodyBinding
            .ReadAsync<ImageGenerationOperatorRequest>(httpContext, cancellationToken)
            .ConfigureAwait(false);

        if (bindError is not null)
        {
            return bindError;
        }

        var request = bound!;

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return TypedResults.Problem(
                title: "Prompt empty",
                detail: "'prompt' is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var options = imageOptions.Value;
        var count = request.Count ?? 1;

        if (count < 1)
        {
            return TypedResults.Problem(
                title: "Image count invalid",
                detail: "'count' must be a positive integer.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (count > options.MaxImagesPerRequest)
        {
            return TypedResults.Problem(
                title: "Image count too high",
                detail: $"The image count is {count}; the limit is {options.MaxImagesPerRequest}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseSize(request.Size, out var size, out var sizeKey))
        {
            return TypedResults.Problem(
                title: "Image size invalid",
                detail: "'size' must use the WIDTHxHEIGHT form, for example '1024x1024'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var generated = await imageGeneratorResolver
                .Resolve()
                .GenerateAsync(
                    new ImageGenerationRequest(request.Prompt),
                    new ImageGenerationOptions
                    {
                        Count = count,
                        ImageSize = size,
                        ModelId = options.Model,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (generated.Contents.Count == 0)
            {
                throw new AgentPrismException("The image provider returned no image content.");
            }

            var usage = pricing.Measure(
                options.Provider!,
                options.Model!,
                sizeKey,
                generated.Contents.Count,
                generated.Usage?.OutputTokenCount);

            var attachments = await attachmentWriter
                .WriteAsync(
                    generated.Contents,
                    tenantContext.TenantId,
                    request.SessionId,
                    runId: null,
                    createdBy: actorResolver.Resolve(),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Ok(new ImageGenerationOperatorResponse
            {
                Attachments = attachments,
                Usage = new ToolCallUsage
                {
                    Unit = usage.Unit,
                    Quantity = usage.Quantity,
                    Cost = usage.Cost,
                    Currency = usage.Currency,
                },
            });
        }
        catch (AgentPrismException exception)
        {
            return TypedResults.Problem(
                title: "Image could not be generated",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(
                title: "Image could not be generated",
                detail: "The image provider or generated-image address could not be reached.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Provider SDKs do not share one exception base type. OpenAI, Azure,
            // and Google can all report an expected provider rejection with a
            // different concrete exception. Letting one escape would turn a
            // provider failure into an unhandled 500 instead of the documented
            // operator-facing 502.
            return TypedResults.Problem(
                title: "Image could not be generated",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool TryParseSize(string? value, out Size? size, out string? key)
    {
        size = null;
        key = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var separator = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);

        if (separator < 1 || separator == value.Length - 1 ||
            !int.TryParse(value[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
            width < 1 || height < 1)
        {
            return false;
        }

        size = new Size(width, height);
        key = $"{width}x{height}";
        return true;
    }
}
#pragma warning restore MEAI001
