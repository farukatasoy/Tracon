using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Validates a tool call's arguments before the call runs.
/// </summary>
/// <remarks>
/// <para>
/// Binding a call's JSON arguments onto <see cref="AIFunctionArguments"/>
/// already rejects a type mismatch, a missing <c>required</c> field, or an
/// invalid <c>enum</c> value — but it never rejects an extra field the schema
/// does not declare, and a hand-written <see cref="AIFunction"/> or an
/// MCP-sourced tool shares none of that behavior. This interface is the
/// consumer's own gate on top of binding, applied uniformly to every tool
/// source.
/// </para>
/// <para>
/// The default implementation is a no-op (registered with
/// <c>TryAddSingleton</c>), so an installation that registers nothing keeps
/// today's behavior exactly. A consumer replaces the registration to enforce
/// its own rule; AgentPrism does not ship a built-in JSON Schema validator —
/// validation stays inside the consumer's own trust boundary.
/// </para>
/// <para>
/// If this validator throws, the call is <strong>rejected</strong>
/// (fail-closed). A gate that fails open on an exception is not a gate.
/// </para>
/// <para>
/// <strong>Tenant mode:</strong> <see cref="ValidateAsync"/> carries no
/// tenant parameter — no delivery guarantee applies to the call it makes
/// (it runs synchronously, inline, exactly once per tool call attempt; the
/// validator is never queued or retried by AgentPrism itself). A
/// multi-tenant implementation reads the ambient tenant itself, the same
/// way the tool authorization wrapper reads it for
/// <see cref="IToolAuthorizationHandler"/>.
/// </para>
/// </remarks>
public interface IToolArgumentsValidator
{
    /// <summary>Validates the arguments of one call before it runs.</summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="arguments">The call's bound arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation outcome.</returns>
    ValueTask<ToolArgumentsValidationResult> ValidateAsync(
        ToolDescriptor tool,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken = default);
}
