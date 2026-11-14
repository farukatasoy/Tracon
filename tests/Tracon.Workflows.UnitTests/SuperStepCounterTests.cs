using Microsoft.Agents.AI.Workflows;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What counts against the super-step limit, on the sequences a run of an agent chain really starts with.
/// </summary>
/// <remarks>
/// The runner sends the input and then the turn token. The engine's loop is already running when the input arrives, so now
/// and then (about one start in 250 with eight runs at once) it takes the input alone as its first step and the token as a
/// second one: the same executor is active in both, because the agent only keeps the input until the token comes. The
/// sequences below are what a chain of three agents reported, read from the engine's events: a normal start, and a start
/// split in two.
/// </remarks>
public sealed class SuperStepCounterTests
{
    private const string First = "agent0";
    private const string Second = "agent1";
    private const string Third = "agent2";
    private const string Output = "OutputMessages";

    [Fact]
    public void A_start_that_the_engine_split_in_two_costs_the_steps_of_a_start_that_was_not()
    {
        // 0: agent0 (input only) · 1: agent0 (the token) · 2: agent1 · 3: agent2 · 4: the output. Four steps count: the chain of
        // three needs one more than it has, as the validator says, whether or not the start was split.
        var counter = new SuperStepCounter(maxSuperSteps: 4, freshStart: true);

        counter.Exceeds(Started(0, First)).ShouldBeFalse();
        counter.Exceeds(Started(1, First)).ShouldBeFalse("the step that only carried the token is not a step of the chain");
        counter.Exceeds(Started(2, Second)).ShouldBeFalse();
        counter.Exceeds(Started(3, Third)).ShouldBeFalse();
        counter.Exceeds(Started(4, Output)).ShouldBeFalse();
        counter.Exceeds(Started(5, Output)).ShouldBeTrue("the fifth that counts passes a limit of four");
    }

    [Fact]
    public void A_start_that_was_not_split_counts_every_step_as_before()
    {
        var counter = new SuperStepCounter(maxSuperSteps: 3, freshStart: true);

        counter.Exceeds(Started(0, First)).ShouldBeFalse();
        counter.Exceeds(Started(1, Second)).ShouldBeFalse();
        counter.Exceeds(Started(2, Third)).ShouldBeFalse();
        counter.Exceeds(Started(3, Output)).ShouldBeTrue("a chain of three agents with a limit of three stops at the output step");
    }

    [Fact]
    public void The_first_two_steps_of_a_run_that_was_not_started_fresh_are_both_counted()
    {
        // A resume or an answer starts from a checkpoint: the same executor active twice in a row is a loop there.
        var counter = new SuperStepCounter(maxSuperSteps: 2, freshStart: false);

        counter.Exceeds(Started(0, First)).ShouldBeFalse();
        counter.Exceeds(Started(1, First)).ShouldBeFalse();
        counter.Exceeds(Started(2, First)).ShouldBeTrue();
    }

    [Fact]
    public void Two_different_executors_in_the_first_steps_are_two_steps_and_a_second_step_with_nothing_known_is_counted()
    {
        var counter = new SuperStepCounter(maxSuperSteps: 2, freshStart: true);

        counter.Exceeds(Started(0, First)).ShouldBeFalse();
        counter.Exceeds(Started(1, Second)).ShouldBeFalse();
        counter.Exceeds(Started(2, Third)).ShouldBeTrue();

        var unknown = new SuperStepCounter(maxSuperSteps: 1, freshStart: true);

        unknown.Exceeds(new SuperStepStartedEvent(0)).ShouldBeFalse();
        unknown.Exceeds(new SuperStepStartedEvent(1)).ShouldBeTrue("a step that names no executor cannot be told from the first");
    }

    [Fact]
    public void A_loop_that_activates_one_executor_for_ever_still_stops_at_the_limit()
    {
        // The split is forgiven once: an executor that goes on alone stops one step later than it would, not never.
        var counter = new SuperStepCounter(maxSuperSteps: 5, freshStart: true);
        var exceeded = false;

        for (var step = 0; step < 20 && !exceeded; step++)
        {
            exceeded = counter.Exceeds(Started(step, First));
        }

        exceeded.ShouldBeTrue();
    }

    private static SuperStepStartedEvent Started(int step, string active)
        => new(step, new SuperStepStartInfo(new HashSet<string>(StringComparer.Ordinal) { active }));
}
