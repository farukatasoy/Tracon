using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace Tracon;

// Google.GenAI 1.16.0 does not ship an MEAI image adapter. This narrow adapter
// implements only IImageGenerator.GenerateAsync; editing and upscaling need
// attachment authorization rules and are intentionally out of this phase.
//
// 🚨 The image is produced with GenerateContentAsync, NOT GenerateImagesAsync.
// The latter targets the Imagen `:predict` surface, which the Gemini API no
// longer serves: measured against a live key, EVERY generation answered 502,
// ListModels returned 58 models and NONE of the six image models supported
// `predict`, and the SDK's own run log says GenerateImagesAsync is deprecated
// in favour of GenerateContentAsync with an image model (K-835).
#pragma warning disable MEAI001
internal sealed class GoogleImageGenerator : IImageGenerator
{
    /// <summary>
    /// The response modality that makes an image model return image bytes.
    /// </summary>
    /// <remarks>
    /// Without it the model answers with text about the prompt instead of an
    /// image, and the call succeeds while producing nothing usable.
    /// </remarks>
    private static readonly List<string> ImageModality = ["IMAGE"];

    private readonly Client _client;

    public GoogleImageGenerator(Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= new ImageGenerationOptions();

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new TraconException("Image-generation prompt cannot be empty.");
        }

        if (request.OriginalImages?.Any() == true)
        {
            throw new TraconException(
                "Google image editing is not supported. The generate_image tool accepts a prompt only.");
        }

        if (options.ImageSize is not null)
        {
            // Gemini's ImageConfig separates aspect ratio and size tiers (for
            // example, 1K/2K). A System.Drawing.Size has no lossless mapping to
            // that provider contract. Passing a guessed tier would also make a
            // configured size multiplier lie about the cost.
            throw new TraconException(
                "Google image generation does not accept WIDTHxHEIGHT. Omit 'size' and use the provider default.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new TraconException("Google image-generation model is required.");
        }

        var config = new GenerateContentConfig
        {
            ResponseModalities = ImageModality,
            CandidateCount = options.Count,
        };

        if (!string.IsNullOrWhiteSpace(options.MediaType))
        {
            config.ImageConfig = new ImageConfig { OutputMimeType = options.MediaType };
        }

        var response = await _client.Models
            .GenerateContentAsync(options.ModelId, request.Prompt, config, cancellationToken)
            .ConfigureAwait(false);

        var contents = new List<AIContent>();

        foreach (var candidate in response.Candidates ?? [])
        {
            foreach (var part in candidate.Content?.Parts ?? [])
            {
                if (part.InlineData is not { } blob || blob.Data is not { Length: > 0 } bytes)
                {
                    continue;
                }

                var mediaType = blob.MimeType ?? "image/png";

                // 🚨 Verified, not assumed. ImageConfig.OutputMimeType is a
                // request, and an image model may answer in its own format. A
                // caller that asked for image/jpeg and silently received
                // image/png would write the wrong extension and ship a file
                // its own consumers cannot open, with nothing in the response
                // saying so.
                if (!string.IsNullOrWhiteSpace(options.MediaType)
                    && !string.Equals(mediaType, options.MediaType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new TraconException(
                        $"Google returned '{mediaType}' for a request that asked for '{options.MediaType}'. " +
                        "The model chose its own format; omit 'mediaType' to accept whatever it returns.");
                }

                contents.Add(new DataContent(bytes, mediaType));
            }
        }

        if (contents.Count == 0)
        {
            // The call succeeded and carried no image. Silence here would look
            // like an empty result rather than a failure.
            throw new TraconException("Google image generation returned no image bytes.");
        }

        return new ImageGenerationResponse(contents);
    }

    public object? GetService(System.Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return null;
    }

    public void Dispose()
    {
        // GoogleChatClientFactory owns the shared Client and disposes it with DI.
    }
}
#pragma warning restore MEAI001
