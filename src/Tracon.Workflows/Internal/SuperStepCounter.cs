using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// Counts the super-steps of a run against <see cref="TraconWorkflowOptions.MaxSuperSteps"/>, and does not count the one that only
/// carried the turn token of a start the engine split in two.
/// </summary>
/// <remarks>
/// <para>
/// The runner starts a run by sending the input and then the turn token. The engine's loop is already running when the input
/// arrives, so now and then (measured at about one start in 250 with eight runs at once) it takes the input alone as its first
/// step, and the token as a second one. An agent only keeps the input until the token comes, so the same executor is the only
/// one active in both steps, and the chain then takes one super-step more than the count the validator promises (a chain of N
/// agents needs N + 1, and the split start made it N + 2: a run at the limit failed on one start in 250).
/// </para>
/// <para>
/// The first step of a fresh start is remembered, and the second is not counted when it activates the same executors. A start that
/// is not fresh (a resume, or an answer to a request) counts every step: a checkpoint's executors may legitimately be active twice
/// in a row there. An executor that goes on alone stops at the limit one step later than it would, not never.
/// </para>
/// </remarks>
/// <param name="maxSuperSteps">The limit.</param>
/// <param name="freshStart">Whether the run starts from its input, and not from a checkpoint.</param>
internal sealed class SuperStepCounter(int maxSuperSteps, bool freshStart)
{
    private int _counted;
    private HashSet<string>? _firstStepExecutors;

    /// <summary>Counts a super-step that is starting.</summary>
    /// <param name="started">The event the engine raised for it.</param>
    /// <returns><see langword="true"/> when the steps that count are now more than the limit.</returns>
    public bool Exceeds(SuperStepStartedEvent started)
    {
        ArgumentNullException.ThrowIfNull(started);

        if (freshStart)
        {
            var active = started.StartInfo?.SendingExecutors;

            if (started.StepNumber == 0)
            {
                _firstStepExecutors = active is { Count: > 0 } ? new HashSet<string>(active, StringComparer.Ordinal) : null;
            }
            else if (started.StepNumber == 1 && _firstStepExecutors is { } first && active is { } second && first.SetEquals(second))
            {
                return false;
            }
        }

        _counted++;

        return _counted > maxSuperSteps;
    }
}
