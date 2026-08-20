namespace AgentPrism;

/// <summary>
/// The size limits that keep <see cref="ToolArgumentCondition"/> a comparison, not a
/// rule engine.
/// </summary>
/// <remarks>
/// Shared by the write-time validation (<c>AgentPrism.AspNetCore</c>) and the
/// evaluation-time matcher (<c>AgentPrism.Core</c>); both must agree on the same
/// numbers, so they are declared once, here.
/// </remarks>
public static class ToolArgumentConditionLimits
{
    /// <summary>The most conditions a single rule can carry.</summary>
    public const int MaxConditions = 10;

    /// <summary>
    /// The most values an <see cref="ToolArgumentOperator.In"/>/<see
    /// cref="ToolArgumentOperator.NotIn"/> list can carry.
    /// </summary>
    public const int MaxListLength = 50;

    /// <summary>The longest an argument path can be, in characters.</summary>
    public const int MaxPathLength = 200;

    /// <summary>The most dotted segments an argument path can carry.</summary>
    public const int MaxPathSegments = 10;
}
