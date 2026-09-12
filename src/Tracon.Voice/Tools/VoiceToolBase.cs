using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Common base for voice tools: dependency resolution and schema handling.
/// </summary>
/// <remarks>
/// <para>
/// Tools are hand-derived, NOT written with <c>AIFunctionFactory</c>. The factory uses
/// reflection and carries <c>[RequiresUnreferencedCode]</c> +
/// <c>[RequiresDynamicCode]</c>; that path is closed because the package is marked AOT
/// compatible. The JSON schema is hand-written for the same reason — the three tools
/// have three parameters in total, so the cost is low.
/// </para>
/// <para>
/// <strong>Dependencies are taken at SETUP time, not at call time.</strong>
/// <c>AIFunctionArguments.Services</c> <strong>cannot be used</strong> in
/// Tracon's pipeline: measured against a running application — Microsoft Agent
/// Framework passes the tool a <c>Microsoft.Extensions.AI.EmptyServiceProvider</c>
/// and no service resolves. The error appears only on a REAL tool call; a unit test
/// passes a fake provider and does not catch it.
/// </para>
/// </remarks>
internal abstract class VoiceToolBase : AIFunction
{
    private readonly IServiceProvider _services;

    /// <summary>Takes the schema and the service provider.</summary>
    /// <param name="services">The service provider at setup time.</param>
    /// <param name="schema">JSON schema of the arguments.</param>
    protected VoiceToolBase(IServiceProvider services, string schema)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        JsonSchema = JsonSerializer.Deserialize(schema, VoiceToolJsonContext.Default.JsonElement);
    }

    /// <inheritdoc />
    public override JsonElement JsonSchema { get; }

    /// <summary>Resolves a service.</summary>
    /// <typeparam name="T">Service type.</typeparam>
    /// <returns>The resolved service.</returns>
    /// <remarks>
    /// Resolved from the root provider. All services the tool needs
    /// (<c>IAttachmentStore</c>, <c>ITenantContext</c>, <c>AttachmentTypeGuard</c>)
    /// are singletons; tenant information comes from <c>IHttpContextAccessor</c>
    /// inside <c>ITenantContext</c>, not from a scope.
    /// </remarks>
    protected T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>Reads a required text argument.</summary>
    /// <param name="arguments">Call arguments.</param>
    /// <param name="name">Argument name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="TraconException">When the argument is missing or empty.</exception>
    protected static string RequireText(AIFunctionArguments arguments, string name)
        => OptionalText(arguments, name)
           ?? throw new TraconException($"The '{name}' argument is required and cannot be empty.");

    /// <summary>Reads an optional text argument.</summary>
    /// <param name="arguments">Call arguments.</param>
    /// <param name="name">Argument name.</param>
    /// <returns>The value; <see langword="null"/> when absent.</returns>
    /// <remarks>
    /// The value can arrive as a <see cref="JsonElement"/>: model arguments
    /// come from JSON and the binder resolves them without static type info.
    /// </remarks>
    protected static string? OptionalText(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        var text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            _ => raw.ToString(),
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

/// <summary>
/// Source-generated context for resolving tool schemas in an AOT-compatible way.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonElement))]
internal sealed partial class VoiceToolJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
