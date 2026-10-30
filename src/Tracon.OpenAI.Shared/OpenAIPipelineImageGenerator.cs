using System.Buffers;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Tracon;

// 🚨 Why the protocol level (measured 2026-10-19, same cause as OpenAIAudioTransport).
//
// OpenAI 2.14.0 was built against System.ClientModel 1.15.0. System.ClientModel 1.16.0 renamed
// AsyncStreamingClientResult<T> to AsyncStreamingResult<T>, and a host that also uses Tracon.Azure
// resolves 1.16.0. ImageClient's member signatures still name the old type, so Native AOT cannot
// compile a host that merely constructs one: ilc stops with "Failed to load type
// 'System.ClientModel.AsyncStreamingClientResult`1'". Measured with the provider AOT smoke sample
// built against these sources: the publish failed with GetImageClient(...).AsIImageGenerator() and
// is clean with this class. Re-measure on the next OpenAI or System.ClientModel upgrade.
#pragma warning disable MEAI001 // Microsoft.Extensions.AI marks its image surface experimental.

/// <summary>
/// Generates images through the OpenAI images API over a client's own pipeline, without
/// <c>ImageClient</c>.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is the one every sub-client of the client uses, so authentication, retries, the
/// network timeout and the transport the client was built with all apply. The OpenAI and the Azure
/// OpenAI v1 endpoints take the same request: the deployment name is the model.
/// </para>
/// <para>
/// The request carries what <see cref="ImageGenerationOptions"/> names: count, size, media type and
/// response format. <see cref="ImageGenerationOptions.RawRepresentationFactory"/> is not read: it
/// returns a type of the OpenAI client library, which this class does not use.
/// </para>
/// </remarks>
internal sealed class OpenAIPipelineImageGenerator : IImageGenerator
{
    private readonly ClientPipeline _pipeline;
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly ImageGeneratorMetadata _metadata;

    /// <summary>Creates a generator over a client's pipeline.</summary>
    /// <param name="client">The shared OpenAI client.</param>
    /// <param name="endpoint">The API base the client was built with.</param>
    /// <param name="model">The image model or Azure deployment. The generator is bound to it.</param>
    /// <param name="providerName">The provider name the metadata reports.</param>
    public OpenAIPipelineImageGenerator(OpenAIClient client, Uri endpoint, string model, string providerName)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _pipeline = client.Pipeline;

        // A relative path resolves against a base that ends in a slash, or it replaces the last segment.
        _endpoint = endpoint.AbsoluteUri.EndsWith('/') ? endpoint : new Uri(endpoint.AbsoluteUri + "/");
        _model = model;
        _metadata = new ImageGeneratorMetadata(providerName, _endpoint, model);
    }

    /// <inheritdoc />
    public async Task<ImageGenerationResponse> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prompt = request.Prompt ?? throw new ArgumentException("The image request has no prompt.", nameof(request));

        BinaryData answer;

        if (request.OriginalImages?.FirstOrDefault() is { } original)
        {
            using var form = EditForm(prompt, original, options);
            var contentType = form.Headers.ContentType!.ToString();
            var payload = await form.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            using var content = BinaryContent.Create(BinaryData.FromBytes(payload));
            answer = await PostAsync("images/edits", content, contentType, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var content = BinaryContent.Create(GenerationBody(prompt, options));
            answer = await PostAsync("images/generations", content, "application/json", cancellationToken).ConfigureAwait(false);
        }

        return ToResponse(answer);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is not null ? null
            : serviceType == typeof(ImageGeneratorMetadata) ? _metadata
            : serviceType.IsInstanceOfType(this) ? this
            : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The pipeline belongs to the shared client.
    }

    private BinaryData GenerationBody(string prompt, ImageGenerationOptions? options)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _model);
            writer.WriteString("prompt", prompt);
            writer.WriteNumber("n", options?.Count ?? 1);

            if (Size(options) is { } size)
            {
                writer.WriteString("size", size);
            }

            if (OutputFormat(options) is { } format)
            {
                writer.WriteString("output_format", format);
            }

            if (ResponseFormat(options) is { } responseFormat)
            {
                writer.WriteString("response_format", responseFormat);
            }

            writer.WriteEndObject();
        }

        return BinaryData.FromBytes(buffer.WrittenMemory);
    }

    private MultipartFormDataContent EditForm(string prompt, AIContent original, ImageGenerationOptions? options)
    {
        if (original is not DataContent image)
        {
            throw new NotSupportedException(
                "An image edit needs the original image as DataContent; a URI or a hosted file is not uploaded.");
        }

        var form = new MultipartFormDataContent
        {
            { new StringContent(_model), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent((options?.Count ?? 1).ToString(CultureInfo.InvariantCulture)), "n" },
        };

        if (Size(options) is { } size)
        {
            form.Add(new StringContent(size), "size");
        }

        if (OutputFormat(options) is { } format)
        {
            form.Add(new StringContent(format), "output_format");
        }

        if (ResponseFormat(options) is { } responseFormat)
        {
            form.Add(new StringContent(responseFormat), "response_format");
        }

        var bytes = MemoryMarshal.TryGetArray(image.Data, out var segment)
            ? new ByteArrayContent(segment.Array!, segment.Offset, segment.Count)
            : new ByteArrayContent(image.Data.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue(image.MediaType);
        form.Add(bytes, "image", image.Name ?? "image" + Extension(image.MediaType));

        return form;
    }

    private async Task<BinaryData> PostAsync(string path, BinaryContent content, string contentType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var message = _pipeline.CreateMessage();
        message.Apply(new RequestOptions { CancellationToken = cancellationToken });

        var request = message.Request;
        request.Method = "POST";
        request.Uri = new Uri(_endpoint, path);
        request.Headers.Set("Content-Type", contentType);
        request.Content = content;

        try
        {
            await _pipeline.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
        {
            // The same measurement as the audio transport: a call cancelled while the transport
            // reads the request surfaces as an ObjectDisposedException. The cause is the cancellation.
            throw new OperationCanceledException(cancellationToken);
        }

        var response = message.Response
            ?? throw new TraconException("The OpenAI client returned no response.");

        // The same exception type ImageClient threw, so a caller's error handling does not change.
        return response.IsError
            ? throw await ClientResultException.CreateAsync(response).ConfigureAwait(false)
            : response.Content;
    }

    private static ImageGenerationResponse ToResponse(BinaryData answer)
    {
        using var document = JsonDocument.Parse(answer);
        var root = document.RootElement;

        var mediaType = root.TryGetProperty("output_format", out var format) && format.ValueKind == JsonValueKind.String
            ? "image/" + format.GetString()
            : "image/png";

        var contents = new List<AIContent>();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("b64_json", out var bytes) && bytes.ValueKind == JsonValueKind.String)
                {
                    contents.Add(new DataContent(bytes.GetBytesFromBase64(), mediaType));
                }
                else if (item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                {
                    contents.Add(new UriContent(new Uri(url.GetString()!, UriKind.Absolute), mediaType));
                }
                else
                {
                    throw new InvalidOperationException("Generated image does not contain a valid URI or byte array.");
                }
            }
        }

        return new ImageGenerationResponse(contents)
        {
            RawRepresentation = answer,
            Usage = Usage(root),
        };
    }

    private static UsageDetails? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var details = new UsageDetails
        {
            InputTokenCount = Count(usage, "input_tokens"),
            OutputTokenCount = Count(usage, "output_tokens"),
            TotalTokenCount = Count(usage, "total_tokens"),
        };

        // The key names Microsoft.Extensions.AI's own OpenAI image generator used.
        if (usage.TryGetProperty("input_tokens_details", out var input) && input.ValueKind == JsonValueKind.Object)
        {
            details.AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["InputTokenDetails.ImageTokenCount"] = Count(input, "image_tokens") ?? 0,
                ["InputTokenDetails.TextTokenCount"] = Count(input, "text_tokens") ?? 0,
            };
        }

        return details;
    }

    private static long? Count(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) ? count : null;

    private static string? Size(ImageGenerationOptions? options)
        => options?.ImageSize is { } size
            ? string.Create(CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}")
            : null;

    private static string? OutputFormat(ImageGenerationOptions? options)
        => options?.MediaType?.ToUpperInvariant() switch
        {
            "IMAGE/PNG" => "png",
            "IMAGE/JPEG" => "jpeg",
            "IMAGE/WEBP" => "webp",
            _ => null,
        };

    private static string? ResponseFormat(ImageGenerationOptions? options)
        => options?.ResponseFormat switch
        {
            ImageGenerationResponseFormat.Uri => "url",
            ImageGenerationResponseFormat.Data => "b64_json",
            _ => null,
        };

    private static string Extension(string mediaType)
        => mediaType.ToUpperInvariant() switch
        {
            "IMAGE/JPEG" => ".jpg",
            "IMAGE/WEBP" => ".webp",
            _ => ".png",
        };
}
#pragma warning restore MEAI001
