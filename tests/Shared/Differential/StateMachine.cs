using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>The hand-written specification a store is compared with.</summary>
/// <typeparam name="TStep">The step type of one state machine.</typeparam>
/// <remarks>
/// A model is written from the interface documentation and the contract tests. It is never built from a store, never
/// calls one, and never shares a helper with one: when the four stores make the same mistake, only a model that did not
/// copy it can tell.
/// </remarks>
internal interface IStepModel<TStep>
{
    /// <summary>Resolves anything a step leaves to the current state (for example "the version just read").</summary>
    /// <param name="step">The generated step.</param>
    /// <returns>The step both sides execute.</returns>
    TStep Concretize(TStep step);

    /// <summary>Applies the step to the model.</summary>
    /// <param name="step">The concrete step.</param>
    /// <returns>What a correct store returns.</returns>
    Observation Apply(TStep step);
}

/// <summary>Applies steps to one store implementation.</summary>
/// <typeparam name="TStep">The step type of one state machine.</typeparam>
internal interface IStepDriver<TStep>
{
    /// <summary>Applies the step to the store.</summary>
    /// <param name="step">The concrete step.</param>
    /// <returns>What the store returned, or the kind of failure it raised.</returns>
    ValueTask<Observation> ApplyAsync(TStep step);
}

/// <summary>The mismatch a differential run found, with the step and both sides.</summary>
/// <param name="message">The description.</param>
internal sealed class DifferentialMismatchException(string message) : Exception(message);

/// <summary>One family of generated sequences: its generator, its model and its driver.</summary>
/// <typeparam name="TPlan">The generated value: the universe of keys and the steps over it.</typeparam>
/// <typeparam name="TStep">The step type.</typeparam>
internal abstract partial class StateMachine<TPlan, TStep>
{
    /// <summary>Gets the name used in failure messages.</summary>
    public abstract string Name { get; }

    /// <summary>Generates a plan.</summary>
    /// <param name="maxSteps">The longest sequence.</param>
    /// <param name="traits">The provider under test; it decides which spellings are twins.</param>
    /// <returns>The generator.</returns>
    public abstract Gen<TPlan> Plans(int maxSteps, ProviderTraits traits);

    /// <summary>Gets the steps of a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The steps in order.</returns>
    public abstract IReadOnlyList<TStep> Steps(TPlan plan);

    /// <summary>Creates the model for a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="traits">The provider under test.</param>
    /// <returns>A model in its initial (empty) state.</returns>
    public abstract IStepModel<TStep> NewModel(TPlan plan, ProviderTraits traits);

    /// <summary>Creates the driver for a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="stores">The stores under test.</param>
    /// <param name="scope">The tenant namespace of the sequence.</param>
    /// <returns>A driver.</returns>
    public abstract IStepDriver<TStep> NewDriver(TPlan plan, DifferentialStores stores, DifferentialScope scope);

    /// <summary>Renders a step for a failure message.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The text.</returns>
    public abstract string Describe(TStep step);

    /// <summary>Renders a plan for a failure message.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The text.</returns>
    public abstract string Print(TPlan plan);

    /// <summary>Runs the plan against the model and the store.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="stores">The stores under test.</param>
    /// <returns>The completion task.</returns>
    /// <exception cref="DifferentialMismatchException">The store returned something the model did not.</exception>
    public async Task ReplayAsync(TPlan plan, DifferentialStores stores)
    {
        ArgumentNullException.ThrowIfNull(stores);

        var scope = DifferentialScope.Next();
        var model = NewModel(plan, stores.Traits);
        var driver = NewDriver(plan, stores, scope);
        var steps = Steps(plan);

        try
        {
            for (var index = 0; index < steps.Count; index++)
            {
                var step = model.Concretize(steps[index]);
                var expected = model.Apply(step);
                Observation actual;

                try
                {
                    actual = await driver.ApplyAsync(step);
                }
                catch (Exception exception) when (exception is not DifferentialMismatchException)
                {
                    actual = Observation.Failure(exception);
                }

                var difference = Observation.Compare(expected, actual);

                if (difference.Length > 0)
                {
                    throw new DifferentialMismatchException(
                        $"{Name} on {stores.Traits.Name}: step {index + 1} of {steps.Count} - {difference}.\n" +
                        $"  step:     {Describe(step)}\n" +
                        $"  expected: {expected}\n" +
                        $"  actual:   {actual}");
                }
            }
        }
        finally
        {
            // A driver that leaves rows other sequences could reach (a job another sequence could lease) cleans up.
            if (driver is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    /// <summary>Generates sequences and replays each one; a mismatch is shrunk and reported with its seed.</summary>
    /// <param name="stores">The stores under test.</param>
    /// <param name="providerIterations">The sequence count to use when the environment does not set one.</param>
    /// <returns>The completion task.</returns>
    public async Task CheckAsync(DifferentialStores stores, long providerIterations)
    {
        ArgumentNullException.ThrowIfNull(stores);

        await Plans(DifferentialSettings.MaxSteps, stores.Traits).SampleAsync(
            plan => ReplayAsync(plan, stores),
            seed: DifferentialSettings.Seed,
            iter: DifferentialSettings.Iterations(providerIterations),
            time: DifferentialSettings.Seconds,
            threads: 1,
            print: Print);
    }
}
