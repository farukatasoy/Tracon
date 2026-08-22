using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

// MEAI 10.9.0 marks its image-generation surface experimental. AgentPrism uses
// the native type directly, not a parallel abstraction, so this narrow file is
// the only Core invocation boundary to revisit when MEAI promotes or changes it.
#pragma warning disable MEAI001
/// <summary>Generates images and returns attachment identifiers to the model.</summary>
/// <remarks>
/// The tool deliberately returns attachment ids, never image bytes or base64. Raw
/// image data can consume an entire model context window in one tool result.
/// </remarks>
internal sealed class GenerateImageTool : AIFunction
{
    /// <summary>The stable tool name used by agent definitions.</summary>
    public const string ToolName = "generate_image";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "prompt": {
              "type": "string",
              "description": "The image to generate."
            },
            "count": {
              "type": "integer",
              "minimum": 1,
              "description": "How many images to generate. Defaults to one."
            },
            "size": {
              "type": "string",
              "pattern": "^[0-9]+x[0-9]+$",
              "description": "Optional provider image size, for example 1024x1024."
            }
          },
          "required": ["prompt"]
        }
        """;

    private readonly IServiceProvider _services;

    public GenerateImageTool(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        JsonSchema = JsonSerializer.Deserialize(Schema, ImageToolJsonContext.Default.JsonElement);
    }

    /// <inheritdoc />
    public override JsonElement JsonSchema { get; }

    /// <inheritdoc />
    public override string Name => ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Generates an image and saves it as an attachment. Returns attachment ids, not image bytes.";

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var prompt = RequireText(arguments, "prompt");
        var count = OptionalCount(arguments, "count") ?? 1;
        var (size, sizeKey) = OptionalSize(arguments, "size");
        var options = Resolve<IOptions<AgentPrismImageOptions>>().Value;

        if (count > options.MaxImagesPerRequest)
        {
            throw new AgentPrismException(
                $"The image count is {count}; the limit is {options.MaxImagesPerRequest}. " +
                "Request fewer images or raise the `AgentPrism:Images:MaxImagesPerRequest` setting.");
        }

        var response = await Resolve<ImageGeneratorResolver>()
            .Resolve()
            .GenerateAsync(
                new ImageGenerationRequest(prompt),
                new ImageGenerationOptions
                {
                    Count = count,
                    ImageSize = size,
                    ModelId = options.Model,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (response.Contents.Count == 0)
        {
            throw new AgentPrismException("The image provider returned no image content.");
        }

        var usage = Resolve<ImagePricing>().Measure(
            options.Provider!,
            options.Model!,
            sizeKey,
            response.Contents.Count,
            response.Usage?.OutputTokenCount);

        // Report before attachment persistence. Image generation can have spent
        // money even when a transient attachment-store outage prevents returning
        // its identifier. Reporting itself is isolated by AgentPrismToolUsage.
        _ = AgentPrismToolUsage.Report(new ToolCallUsage
        {
            Unit = usage.Unit,
            Quantity = usage.Quantity,
            Cost = usage.Cost,
            Currency = usage.Currency,
        });

        var scope = AgentPrismRunContext.Current
            ?? throw new AgentPrismException("Image generation requires an active AgentPrism run scope.");
        var tenantId = scope.TenantId
            ?? throw new AgentPrismException("Image generation requires a tenant-bound AgentPrism run scope.");
        var attachments = await Resolve<ImageAttachmentWriter>()
            .WriteAsync(
                response.Contents,
                tenantId,
                scope.SessionId,
                scope.RunId,
                scope.AgentName,
                cancellationToken)
            .ConfigureAwait(false);

        return "Images produced. attachmentIds=" +
               string.Join(",", attachments.Select(static attachment => attachment.Id.ToString())) +
               $", count={attachments.Count}.";
    }

    private T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    private static string RequireText(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            throw new AgentPrismException($"The '{name}' argument is required and cannot be empty.");
        }

        var text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => raw.ToString(),
        };

        return string.IsNullOrWhiteSpace(text)
            ? throw new AgentPrismException($"The '{name}' argument is required and cannot be empty.")
            : text;
    }

    private static int? OptionalCount(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        var valid = raw switch
        {
            int value => value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var value) => value,
            JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(
                element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            string value when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => throw new AgentPrismException($"The '{name}' argument must be a positive integer."),
        };

        if (valid < 1)
        {
            throw new AgentPrismException($"The '{name}' argument must be a positive integer.");
        }

        return valid;
    }

    private static (Size? Size, string? Key) OptionalSize(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return (null, null);
        }

        var value = raw switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var separator = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);

        if (separator < 1 || separator == value.Length - 1 ||
            !int.TryParse(value[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
            width < 1 || height < 1)
        {
            throw new AgentPrismException($"The '{name}' argument must use the WIDTHxHEIGHT form, for example '1024x1024'.");
        }

        return (new Size(width, height), $"{width}x{height}");
    }
}

[JsonSerializable(typeof(JsonElement))]
internal sealed partial class ImageToolJsonContext : JsonSerializerContext;
#pragma warning restore MEAI001
