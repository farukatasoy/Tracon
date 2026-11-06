using Tracon;

namespace Tracon.CapacityHost;

/// <summary>
/// The judge online evaluation uses under <see cref="CapacityEnvironment.OnlineEval"/>: it gives every run the same score
/// at once and calls no model.
/// </summary>
/// <remarks>
/// What is measured is the state evaluation keeps for each tenant (the sampler's hourly budget, the score window) and the
/// judge job each completed run queues. A judge that called the model would add a second model call to every run and
/// measure the fixture's model delay as much as the evaluation.
/// </remarks>
public sealed class CapacityRunJudge : IRunJudge
{
    /// <summary>The judge's name, written as <c>judge:capacity</c> on its scores.</summary>
    public const string JudgeName = "capacity";

    /// <inheritdoc />
    public string Name => JudgeName;

    /// <inheritdoc />
    public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The headline score carries the judge's own name, which is the one the score window takes.
        return new ValueTask<RunJudgment>(new RunJudgment
        {
            Scores =
            [
                new JudgeScore
                {
                    Name = JudgeName,
                    Kind = RunScoreKind.Numeric,
                    Value = 80,
                    Comment = "Fixed score of the capacity fixture.",
                },
            ],
        });
    }
}
