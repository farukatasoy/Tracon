using System.Text.Json;

namespace Tracon;

/// <summary>
/// One comparison against a tool call argument: <em>path · operator · value</em>.
/// </summary>
/// <remarks>
/// <para>
/// A comparison only, never an expression. There is no <c>OR</c> — a rule's
/// conditions are combined with <c>AND</c>; two rules are written instead of one
/// with an <c>OR</c>. There is no array index in <see cref="Path"/>. This is a
/// deliberate limit ('s sibling): the language must never grow
/// into a rule engine.
/// </para>
/// </remarks>
public sealed record ToolArgumentCondition
{
    /// <summary>
    /// Gets the dotted path into the argument object, for example <c>order.amount</c>.
    /// No array index is accepted.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>Gets the comparison operator.</summary>
    public required ToolArgumentOperator Operator { get; init; }

    /// <summary>
    /// Gets the value compared against. For <see cref="ToolArgumentOperator.In"/> and
    /// <see cref="ToolArgumentOperator.NotIn"/> this is a JSON array of text or numbers;
    /// for every other operator it is a single text, number, or boolean.
    /// </summary>
    public required JsonElement Value { get; init; }
}
