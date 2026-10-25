using System.Text.Json;

namespace Tracon;

/// <summary>
/// The response schema as a provider's client sends it to the model, and what differs from the
/// schema the author wrote.
/// </summary>
/// <remarks>
/// <para>
/// A provider adapter may rewrite a response schema before it reaches the wire. The OpenAI adapters
/// do it for every request, strict or not: they close every object, make every property required and
/// move the bounds (<c>maxLength</c>, <c>pattern</c>, <c>minimum</c> and the like) out of the schema
/// into its <c>description</c> text. The model is asked for the rewritten schema, and a registered
/// <see cref="IStructuredResponseValidator"/> receives the schema as the author wrote it, so this shape is the
/// only place the author can see what the model was asked for.
/// </para>
/// <para>
/// JSON locations in <see cref="OptionalMadeRequired"/> and <see cref="KeywordsNotEnforced"/> are
/// JSON Pointers into the author's schema, with <c>#</c> for the root: <c>#/properties/address</c>.
/// </para>
/// </remarks>
public sealed record ResponseSchemaShape
{
    /// <summary>Gets the schema the provider's client sends to the model.</summary>
    public required JsonElement EffectiveSchema { get; init; }

    /// <summary>
    /// Gets a value that is <see langword="true"/> when the client asks the provider to enforce the
    /// schema while the model generates (the provider's strict mode).
    /// </summary>
    public required bool Strict { get; init; }

    /// <summary>
    /// Gets the properties the author left optional that the client sends as required, and whose
    /// schema does not allow <c>null</c>. The model has to produce a value for each of them.
    /// </summary>
    /// <remarks>
    /// A property that must be allowed to stay empty accepts <c>null</c>, for example
    /// <c>"type": ["string", "null"]</c>.
    /// </remarks>
    public IReadOnlyList<string> OptionalMadeRequired { get => field ?? []; init; }

    /// <summary>
    /// Gets the bounds of the author's schema that the client does not send as bounds, each as
    /// <c>"&lt;location&gt;: &lt;keyword&gt;"</c>, for example <c>#/properties/name: maxLength</c>.
    /// A registered <see cref="IStructuredResponseValidator"/> receives the schema as written and can still apply them.
    /// </summary>
    public IReadOnlyList<string> KeywordsNotEnforced { get => field ?? []; init; }

    /// <summary>
    /// Gets the parts of the schema that the provider refuses to accept in strict mode, each as
    /// <c>"&lt;location&gt;: &lt;keyword&gt;"</c>, for example <c>#/properties/answer: oneOf</c>. Empty when the
    /// schema has none, and also when the provider cannot say.
    /// </summary>
    /// <remarks>
    /// A provider fills it only from a refusal it has measured against the real service. It is read when
    /// <see cref="Strict"/> is <see langword="true"/>: the same schema runs without the keyword in the
    /// provider's non-strict mode. The locations are in the schema the client sends
    /// (<see cref="EffectiveSchema"/>).
    /// </remarks>
    public IReadOnlyList<string> StrictUnsupported { get => field ?? []; init; }
}

/// <summary>
/// An optional interface of an <see cref="IModelProvider"/> that states which response schema its
/// client sends to the model for a binding.
/// </summary>
/// <remarks>
/// <para>
/// The validation report (<c>POST /api/agents/validate</c>) calls it for the binding and for every
/// <see cref="ModelBinding.Fallbacks"/> link, and warns the author about each difference: a
/// property that is optional in the author's schema and required on the wire, a bound that
/// is not sent, and (in strict mode) a part the service refuses. A provider that does not implement this
/// interface adds nothing to the report.
/// </para>
/// <para>
/// The answer has to be computed the way the client computes it for a request. A provider that does
/// not rewrite the schema returns the schema it was given, <see cref="ResponseSchemaShape.Strict"/>
/// from its own setting, and empty lists.
/// </para>
/// <para><strong>Lifetime and threading</strong></para>
/// <para>
/// The registry holds the provider as a <strong>singleton</strong> and the validation path calls the
/// method on it, so it may run concurrently. Keep no state between calls: the validation path is not
/// a hot path, and a cached answer would be a second place that drifts from the client.
/// </para>
/// <para><strong>Tenant mode</strong></para>
/// <para>
/// The answer is <strong>tenant-independent</strong>: it depends on the binding and the schema, not
/// on the tenant's own credential.
/// </para>
/// <para><strong>Delivery</strong></para>
/// <para>
/// A computation over its arguments, with no side effect and no request to the provider: no delivery
/// guarantee applies.
/// </para>
/// </remarks>
public interface IResponseSchemaSupport
{
    /// <summary>Describes the response schema the provider's client sends for a binding.</summary>
    /// <param name="binding">
    /// The binding. A fallback link is described with a binding that carries only its provider and
    /// model, because a link receives none of the primary binding's
    /// <see cref="ModelBinding.ProviderSettings"/>.
    /// </param>
    /// <param name="schema">The schema the author wrote. A JSON object.</param>
    /// <returns>The shape of the schema on the wire.</returns>
    /// <exception cref="TraconException">A setting of the binding has a value of the wrong type.</exception>
    /// <exception cref="ArgumentException">The schema is not a valid schema document.</exception>
    ResponseSchemaShape DescribeResponseSchema(ModelBinding binding, JsonElement schema);
}
