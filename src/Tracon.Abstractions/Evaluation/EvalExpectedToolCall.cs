using System.Text.Json;

namespace Tracon;

/// <summary>A tool call an <see cref="EvalCase"/> expects the agent to make.</summary>
/// <remarks>
/// The <c>toolCallArgsMatch</c> check reads it. The case passes that check when,
/// for every expected call, the run called a tool of that <see cref="Name"/>
/// (letter case does not matter) and - when <see cref="Arguments"/> is given -
/// one such call carried every listed argument with an equal value.
/// </remarks>
public sealed record EvalExpectedToolCall
{
    /// <summary>The tool name. Compared without regard to letter case.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The arguments the call must carry, or <see langword="null"/> to check only
    /// that the tool was called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The listed arguments are a subset: the call may carry others. An argument
    /// name matches without regard to letter case. A value matches by its JSON
    /// meaning: <c>2</c> equals <c>2.0</c>, an object equals an object with the
    /// same properties in any order, and white space between tokens does not
    /// matter. A nested object or array matches exactly - the subset rule applies
    /// to the top-level argument names only.
    /// </para>
    /// <para>
    /// A <c>null</c> value expects the argument to be absent or <c>null</c>: the
    /// check does not see a <c>null</c> argument a model sent.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
}
