namespace Tracon;

/// <summary>
/// Defines automatic rollback and gradual traffic-increase rules for an experiment canary variant.
/// </summary>
/// <remarks>
/// <para>
/// This policy can only be defined for <strong>two-variant</strong> experiments.
/// <see cref="CanaryVariant"/> is the canary and the one remaining variant is the
/// control. With more than two variants, proportional control-weight redistribution
/// could not guarantee session stability. This
/// constraint keeps the canary range fixed at <c>[0, canaryWeight)</c> and makes
/// the control range contiguous.
/// </para>
/// <para>
/// Comparison is <strong>relative</strong>. The canary rolls back when its error
/// rate is <see cref="MaxErrorRateDelta"/> higher than the control rate. An
/// absolute threshold, such as "stop at a 5% error rate", would unfairly stop a
/// canary for an agent whose control is also unhealthy.
/// </para>
/// </remarks>
public sealed record CanaryPolicy
{
    /// <summary>Gets the canary variant name. It must exist in the experiment <c>Variants</c> list.</summary>
    public required string CanaryVariant { get; init; }

    /// <summary>
    /// Gets the maximum absolute error-rate difference from the control, from 0.0
    /// through 1.0. The canary rolls back when its rate is higher. No error-rate
    /// check runs when this value is <see langword="null"/>.
    /// </summary>
    public double? MaxErrorRateDelta { get; init; }

    /// <summary>
    /// Gets the minimum average canary score from 0 through 100. The canary rolls
    /// back below this threshold. No score check runs when this value is
    /// <see langword="null"/>.
    /// </summary>
    public int? MinScore { get; init; }

    /// <summary>
    /// Gets the minimum completed run count that both the canary and control
    /// variants must reach before a decision is made.
    /// </summary>
    /// <remarks>
    /// This is the same rule as
    /// <c>OnlineEvaluationOptions.MinSampleSize</c>. It has the same
    /// default of <c>20</c> and the same rationale: a threshold reacts to noise
    /// with a small sample. Gradual increases use the same value to advance to
    /// the next step; a second threshold property is not added.
    /// </remarks>
    public int MinSampleSize { get; init; } = 20;

    /// <summary>
    /// Gets the canary-weight steps that increase over time, for example
    /// <c>[5, 25, 50, 100]</c>. An empty list disables gradual increases. The
    /// canary weight stays at the value set through <c>SaveAsync</c> and only
    /// rollback is evaluated.
    /// </summary>
    public IReadOnlyList<int> RampSteps { get; init; } = [];

    /// <summary>Gets the minimum time between steps.</summary>
    public TimeSpan RampInterval { get; init; } = TimeSpan.FromHours(1);
}
