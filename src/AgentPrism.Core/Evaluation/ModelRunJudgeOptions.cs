namespace AgentPrism;

/// <summary>Options for the built-in model-based judge (<see cref="ModelRunJudge"/>) — Phase 49.</summary>
/// <remarks>
/// Values do not come from a configuration section. Since <see cref="ModelBinding"/>
/// is a nested type, this project configures it in code through
/// <see cref="AgentPrismOnlineEvaluationBuilderExtensions.AddModelRunJudge"/>.
/// </remarks>
public sealed class ModelRunJudgeOptions
{
    /// <summary>
    /// The judge model.
    /// </summary>
    /// <remarks>
    /// Select this separately from the measured agent model. A lower-cost model is
    /// sufficient and reduces cost. The same model would systematically bias scoring
    /// of its own output, so this is required and has no default.
    /// </remarks>
    public required ModelBinding Model { get; set; }

    /// <summary>The scoring criteria. They form the prompt body.</summary>
    public IList<string> Criteria { get; } = [];

    /// <summary>Additional instructions for the judge.</summary>
    public string? Instructions { get; set; }
}
