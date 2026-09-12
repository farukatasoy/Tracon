namespace Tracon;

/// <summary>
/// Validates a run's response against the agent's requested
/// <see cref="AgentResponseFormat"/> before the run closes.
/// </summary>
/// <remarks>
/// <para>
/// A structural constraint sent to the provider (<see cref="AgentResponseFormat"/>)
/// does not guarantee the response that comes back: a model can still answer with
/// empty content, cut off mid-document, or a JSON value that does not match the
/// requested schema. Tracon checks well-formedness on its own (non-empty,
/// parses as JSON) before this validator ever runs; everything past that —
/// schema conformance, a domain rule such as an allowed <c>score</c> range — is
/// this validator's job.
/// </para>
/// <para>
/// The default implementation is a no-op (registered with
/// <c>TryAddSingleton</c>), so an installation that registers nothing keeps
/// today's behaviour exactly, and <c>TraconStructuredResponseOptions.Enabled</c>
/// stays <see langword="false"/> by default on top of that. A consumer replaces
/// the registration to enforce its own rule; Tracon does not ship a built-in
/// JSON Schema validator — validation stays inside the consumer's own trust
/// boundary, the same position <see cref="IToolArgumentsValidator"/> documents.
/// </para>
/// <para>
/// If this validator throws, the response is <strong>invalid</strong>
/// (fail-closed). A gate that fails open on an exception is not a gate.
/// </para>
/// <para>
/// <strong>Tenant mode:</strong> <see cref="ValidateAsync"/> carries no tenant
/// parameter — no delivery guarantee applies to the call it makes (it runs
/// synchronously, inline, once per run that reaches this check; it is never
/// queued or retried by Tracon itself). A multi-tenant implementation
/// reads the ambient tenant itself, the same way <see cref="IToolArgumentsValidator"/>
/// does.
/// </para>
/// </remarks>
public interface IStructuredResponseValidator
{
    /// <summary>Validates one run's response.</summary>
    /// <param name="context">The response and the format it was asked to match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation outcome.</returns>
    ValueTask<StructuredResponseValidationResult> ValidateAsync(
        StructuredResponseValidationContext context,
        CancellationToken cancellationToken = default);
}
