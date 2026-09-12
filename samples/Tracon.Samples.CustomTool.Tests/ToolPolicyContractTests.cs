using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tracon.Testing.Contracts.Tools;
using Microsoft.Extensions.AI;

namespace Tracon.Samples.CustomTool.Tests;

/// <summary>A tool whose schema carries a required pattern-constrained string and a required bounded integer.</summary>
internal static class OrderSubmissionTool
{
    public static AIFunction Instance { get; } = AIFunctionFactory.Create(
        (Func<string, int, string>)Submit, "submit_order_validated", "Submits an order with a validated shape.");

    private static string Submit(
        [Description("Order code: letters followed by digits (e.g. abc123).")]
        [RegularExpression("^[a-z]+[0-9]+$")]
        string orderId,
        [Description("Units to submit, 1-100.")]
        [Range(1, 100)]
        int quantity)
        => $"submitted {orderId} x{quantity}";
}

/// <summary>
/// A generic validator that reads its rules from the tool's own declared
/// JSON schema, the same approach a consumer without a per-tool hand-written
/// validator would reach for.
/// </summary>
internal sealed class SchemaDrivenArgumentsValidator : IToolArgumentsValidator
{
    public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
        ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = JsonDocument.Parse(tool.JsonSchema ?? "{}");
        var schema = document.RootElement;

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray())
            {
                if (!arguments.Keys.Contains(name.GetString()!, StringComparer.Ordinal))
                {
                    return new ValueTask<ToolArgumentsValidationResult>(
                        ToolArgumentsValidationResult.Invalid($"Missing required argument '{name.GetString()}'."));
                }
            }
        }

        if (!schema.TryGetProperty("properties", out var properties))
        {
            return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Valid);
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in properties.EnumerateObject())
        {
            declared.Add(property.Name);

            if (!arguments.TryGetValue(property.Name, out var raw) || raw is not JsonElement value)
            {
                continue;
            }

            if (!MatchesDeclaredType(value, property.Value))
            {
                return new ValueTask<ToolArgumentsValidationResult>(
                    ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' has the wrong type."));
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                var number = value.GetDouble();

                if (property.Value.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble())
                {
                    return new ValueTask<ToolArgumentsValidationResult>(
                        ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' is below the minimum."));
                }

                if (property.Value.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble())
                {
                    return new ValueTask<ToolArgumentsValidationResult>(
                        ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' is above the maximum."));
                }
            }

            if (value.ValueKind == JsonValueKind.String
                && property.Value.TryGetProperty("pattern", out var pattern)
                && !Regex.IsMatch(value.GetString() ?? string.Empty, pattern.GetString()!, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
            {
                return new ValueTask<ToolArgumentsValidationResult>(
                    ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' does not match the required pattern."));
            }
        }

        foreach (var key in arguments.Keys)
        {
            if (!declared.Contains(key))
            {
                return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Invalid($"Unknown argument '{key}'."));
            }
        }

        return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Valid);
    }

    private static bool MatchesDeclaredType(JsonElement value, JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return true;
        }

        var declaredTypes = typeElement.ValueKind == JsonValueKind.Array
            ? typeElement.EnumerateArray().Select(static entry => entry.GetString())
            : [typeElement.GetString()];

        return declaredTypes.Any(type => type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        });
    }
}

/// <summary>Runs the published argument-validation contract against a real, schema-driven validator.</summary>
public sealed class OrderSubmissionArgumentValidationTests : ToolArgumentValidationContract
{
    protected override ValueTask<AIFunction> CreateToolAsync() => new(OrderSubmissionTool.Instance);

    protected override ValueTask<IToolArgumentsValidator> CreateValidatorAsync() => new(new SchemaDrivenArgumentsValidator());
}

/// <summary>
/// A permission-and-tenant policy: a tenant may call a tool only if it is
/// granted the tool's declared <see cref="ToolAuthorizationRequest.RequiredPermission"/>.
/// </summary>
internal sealed class GrantListToolAuthorizationHandler : IToolAuthorizationHandler
{
    private static readonly Dictionary<string, IReadOnlySet<string>> Grants =
        new(StringComparer.Ordinal)
        {
            ["acme"] = new HashSet<string>(StringComparer.Ordinal) { "orders.submit" },
        };

    public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequiredPermission is null)
        {
            return new ValueTask<ToolAuthorizationResult>(ToolAuthorizationResult.Allow());
        }

        var allowed = Grants.TryGetValue(request.TenantId, out var permissions) && permissions.Contains(request.RequiredPermission);

        return new ValueTask<ToolAuthorizationResult>(
            allowed
                ? ToolAuthorizationResult.Allow()
                : ToolAuthorizationResult.Deny($"'{request.TenantId}' is not granted '{request.RequiredPermission}'."));
    }
}

/// <summary>Runs the published authorization contract against a real, tenant-keyed policy.</summary>
public sealed class OrderSubmissionAuthorizationTests : ToolAuthorizationContract
{
    protected override ValueTask<IToolAuthorizationHandler> CreateHandlerAsync() => new(new GrantListToolAuthorizationHandler());

    protected override ToolAuthorizationRequest DeniedRequest { get; } = new()
    {
        ToolName = "submit_order",
        Effect = ToolEffect.External,
        RequiredPermission = "orders.submit",
        TenantId = "globex",
        RunId = Guid.NewGuid(),
        AgentName = "fulfillment",
    };

    protected override ToolAuthorizationRequest AllowedRequest => DeniedRequest with { TenantId = "acme" };
}
