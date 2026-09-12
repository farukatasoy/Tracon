using System.Text.Json;

namespace Tracon;

/// <summary>
/// An evaluation (eval) suite defined for an agent: carries which agent is
/// measured, with which checks.
/// </summary>
/// <remarks>
/// <c>Checks</c> is declarative: it runs no free-form code, it is
/// only a JSON array that <c>EvalCheckFactory</c> maps to recognized kind
/// names. A custom check is registered on the code side with <c>AddEvalCheck</c>.
/// </remarks>
public sealed record EvalSuite
{
    /// <summary>The suite identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The tenant the suite belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The suite name. Unique within the tenant.</summary>
    public required string Name { get; init; }

    /// <summary>A short description.</summary>
    public string? Description { get; init; }

    /// <summary>The name of the agent this suite measures.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// The check definitions. Example: <c>[{"kind":"nonEmpty","minLength":10}]</c>.
    /// </summary>
    public JsonElement Checks { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
