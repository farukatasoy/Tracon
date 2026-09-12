using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace Tracon;

// Google.GenAI 1.16.0 does not ship an MEAI image adapter. This narrow adapter
// implements only IImageGenerator.GenerateAsync; editing and upscaling need
// attachment authorization rules and are intentionally out of this phase.
#pragma warning disable MEAI001
internal sealed class GoogleImageGenerator : IImageGenerator
{
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
            // Gemini's GenerateImagesConfig separates aspect ratio and size tiers
            // (for example, 1K/2K). A System.Drawing.Size has no lossless mapping
            // to that provider contract. Passing a guessed tier would also make a
            // configured size multiplier lie about the cost.
            throw new TraconException(
                "Google image generation does not accept WIDTHxHEIGHT. Omit 'size' and use the provider default.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new TraconException("Google image-generation model is required.");
        }

        var response = await _client.Models
            .GenerateImagesAsync(
                options.ModelId,
                request.Prompt,
                new GenerateImagesConfig
                {
                    NumberOfImages = options.Count,
                    OutputMimeType = options.MediaType,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var contents = new List<AIContent>();

        foreach (var generated in response.GeneratedImages ?? [])
        {
            var image = generated.Image;

            if (image?.ImageBytes is not { Length: > 0 } bytes)
            {
                throw new TraconException("Google image generation returned no image bytes.");
            }

            contents.Add(new DataContent(bytes, image.MimeType ?? "image/png"));
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
