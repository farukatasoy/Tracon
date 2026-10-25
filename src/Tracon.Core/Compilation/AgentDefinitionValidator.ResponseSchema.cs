using System.Text.Json;

namespace Tracon;

/// <summary>
/// The response-schema check of <see cref="AgentDefinitionValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// A provider adapter can rewrite the schema before it sends it, and the author sees none of it: the
/// OpenAI clients make every property required and move the bounds into <c>description</c> text, strict or
/// not. A provider states the schema it sends through <see cref="IResponseSchemaSupport"/>; this check puts
/// that schema in the report and warns about the two differences that change what the model is asked for.
/// </para>
/// <para>
/// <strong>All three findings are warnings.</strong> The two differences leave the definition running; the model is
/// simply asked for something other than the author wrote. A part the provider's strict mode refuses fails every
/// request with HTTP 400, and is still only a warning: an error would stop the definition from being saved, and the
/// answer is tenant-independent, so a tenant whose own credential reaches a server that accepts the part would be
/// blocked for nothing. The fallback links are described too, with a binding that carries no provider settings,
/// because a link receives none of the primary binding's settings.
/// </para>
/// <para>
/// A schema the provider cannot describe (it is not a valid schema document) adds no entry: the run fails on
/// it at the first request, the same way with or without this check. Whatever the describing throws, the report is
/// still returned.
/// </para>
/// </remarks>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The code of the warning about an optional property the provider sends as required.</summary>
    internal const string ResponseSchemaOptionalRequiredCode = ValidationCodes.ResponseSchemaOptionalRequired;

    /// <summary>The code of the warning about a bound the provider does not send as a bound.</summary>
    internal const string ResponseSchemaKeywordNotEnforcedCode = ValidationCodes.ResponseSchemaKeywordNotEnforced;

    /// <summary>The code of the warning about a part of the schema that the provider's strict mode refuses.</summary>
    internal const string ResponseSchemaStrictUnsupportedCode = ValidationCodes.ResponseSchemaStrictUnsupported;

    // A report line with the whole list would grow with the schema; the entry in `responseSchemas` keeps all of it.
    private const int ListedLocations = 8;

    private const string ResponseSchemaPath = "model.responseFormat.schema";

    private List<ResponseSchemaReport> CheckResponseSchemas(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        var reports = new List<ResponseSchemaReport>();

        if (definition.Model.ResponseFormat is not { Kind: AgentResponseFormatKind.JsonSchema, Schema: { ValueKind: JsonValueKind.Object } schema })
        {
            return reports;
        }

        var providers = _models.List();
        var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DescribeFor(providers, definition.Model, ResponseSchemaPath);

        for (var index = 0; index < definition.Model.Fallbacks.Count; index++)
        {
            var link = definition.Model.Fallbacks[index];

            // A link receives the primary call's request options and none of its provider settings.
            DescribeFor(
                providers,
                new ModelBinding { Provider = link.Provider, Model = link.Model },
                $"model.fallbacks[{index}]");
        }

        return reports;

        void DescribeFor(IReadOnlyList<ModelProviderDescriptor> providers, ModelBinding binding, string path)
        {
            var descriptor = providers.FirstOrDefault(
                candidate => string.Equals(candidate.Name, binding.Provider, StringComparison.OrdinalIgnoreCase));

            if (descriptor?.ResponseSchemaSupport is not { } support)
            {
                return;
            }

            ResponseSchemaShape shape;

            try
            {
                shape = support.DescribeResponseSchema(binding, schema);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A setting of the wrong type is reported by CheckModelAsync. A schema the provider's client cannot
                // transform (ArgumentException, or InvalidOperationException for a non-text description beside a
                // default) fails the run at the first request; the report must not turn into a 500 meanwhile.
                return;
            }

            reports.Add(new ResponseSchemaReport
            {
                Provider = descriptor.Name,
                Model = binding.Model,
                Strict = shape.Strict,
                Schema = shape.EffectiveSchema,
            });

            // The service answers HTTP 400 to every request, but this is a warning, not an error: an error would
            // stop the definition from being saved, and the check is tenant-independent. A tenant whose own
            // credential points at a server that accepts these parts would be blocked for nothing. Only the
            // binding's own provider can be strict: a link receives none of the provider settings.
            if (shape.Strict && shape.StrictUnsupported.Count > 0)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ResponseSchemaStrictUnsupportedCode,
                    Message = $"The '{descriptor.Name}' provider asks the service to enforce the response schema (strict mode), " +
                              $"and the service refuses these parts of it with HTTP 400: {Listed(shape.StrictUnsupported)}. " +
                              "Every request of this definition fails until they are rewritten with keywords strict mode " +
                              "accepts (anyOf instead of oneOf, an object with its properties spelled out and " +
                              "additionalProperties false), or the strict setting is turned off.",
                    Path = path,
                });
            }

            // One warning per provider: two links to the same provider would repeat the same sentence.
            if (!warned.Add(descriptor.Name))
            {
                return;
            }

            if (shape.OptionalMadeRequired.Count > 0)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ResponseSchemaOptionalRequiredCode,
                    Message = $"The '{descriptor.Name}' provider sends the response schema with every property required, " +
                              $"so the model must produce a value for these properties that the schema leaves optional: " +
                              $"{Listed(shape.OptionalMadeRequired)}. To let a property stay empty, allow null in its " +
                              "schema, for example \"type\": [\"string\", \"null\"].",
                    Path = path,
                });
            }

            if (shape.KeywordsNotEnforced.Count > 0)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ResponseSchemaKeywordNotEnforcedCode,
                    Message = $"The '{descriptor.Name}' provider does not send these bounds of the response schema as " +
                              $"bounds: {Listed(shape.KeywordsNotEnforced)}. It writes them into the property's description " +
                              "text, so the model may break them. A registered IStructuredResponseValidator still " +
                              "receives the schema as written.",
                    Path = path,
                });
            }
        }
    }

    private static string Listed(IReadOnlyList<string> locations)
        => locations.Count <= ListedLocations
            ? string.Join(", ", locations)
            : $"{string.Join(", ", locations.Take(ListedLocations))} and {locations.Count - ListedLocations} more";
}
