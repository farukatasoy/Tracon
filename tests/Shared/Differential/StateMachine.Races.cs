using System.Text;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>The concurrent form of a state machine: a few steps run in several tasks at once.</summary>
/// <typeparam name="TPlan">The generated value: the universe of keys and the steps over it.</typeparam>
/// <typeparam name="TStep">The step type.</typeparam>
internal abstract partial class StateMachine<TPlan, TStep>
{
    /// <summary>
    /// Gets the steps of a plan in a form that is safe to run from several tasks at once: every step runs under one
    /// ambient tenant, because the tenant context the stores read is a single shared value.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The steps.</returns>
    public virtual IReadOnlyList<TStep> RaceSteps(TPlan plan) => Steps(plan);

    /// <summary>
    /// Gets the steps that read back everything a race could have changed, run one after the other once the tasks end.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The steps.</returns>
    public virtual IReadOnlyList<TStep> Snapshot(TPlan plan) => [];

    /// <summary>
    /// Gets how many leading steps run one after the other before the tasks start, or <see langword="null"/> to let the
    /// generator choose.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The count, or <see langword="null"/>.</returns>
    public virtual int? RaceSetupLength(TPlan plan) => null;

    /// <summary>
    /// Runs generated sequences with a few steps in several tasks at once, and requires that the observed results are
    /// those of some serial order of the model.
    /// </summary>
    /// <param name="stores">The stores under test.</param>
    /// <param name="providerIterations">The plan count to use when the environment does not set one.</param>
    /// <param name="repeats">How many times each plan's concurrent phase runs, each time in a fresh tenant namespace.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// The oracle is the same hand-written model as the sequential check. Two tasks that both read version 3 of a record
    /// and both try to replace it must produce results that one of the two serial orders explains (one wins, one
    /// loses); a store in which both win has no such order.
    /// </remarks>
    public async Task CheckConcurrentAsync(DifferentialStores stores, long providerIterations, int repeats)
    {
        ArgumentNullException.ThrowIfNull(stores);

        var plans =
            from plan in Plans(10, stores.Traits)
            from setup in Gen.Int[0, 3]
            from tasks in Gen.Int[2, 3]
            select (plan, setup, tasks);

        await plans.SampleAsync(
            async value =>
            {
                for (var repeat = 0; repeat < repeats; repeat++)
                {
                    await RaceOnceAsync(value.plan, value.setup, value.tasks, stores);
                }
            },
            seed: DifferentialSettings.Seed,
            iter: Math.Max(1, DifferentialSettings.Iterations(providerIterations) / 4),
            time: DifferentialSettings.Seconds,
            threads: 1,
            print: value => $"setup {value.setup} steps, {value.tasks} tasks\n{Print(value.plan)}");
    }

    private static async Task<Observation> ApplyCatchingAsync(IStepDriver<TStep> driver, TStep step)
    {
        try
        {
            return await driver.ApplyAsync(step);
        }
        catch (Exception exception)
        {
            return Observation.Failure(exception);
        }
    }

    private static IEnumerable<int[]> Interleavings(int[] laneLengths)
    {
        var total = laneLengths.Sum();
        var order = new int[total];
        var used = new int[laneLengths.Length];

        IEnumerable<int[]> Walk(int depth)
        {
            if (depth == total)
            {
                yield return (int[])order.Clone();

                yield break;
            }

            for (var lane = 0; lane < laneLengths.Length; lane++)
            {
                if (used[lane] == laneLengths[lane])
                {
                    continue;
                }

                used[lane]++;
                order[depth] = lane;

                foreach (var found in Walk(depth + 1))
                {
                    yield return found;
                }

                used[lane]--;
            }
        }

        return Walk(0);
    }

    /// <summary>
    /// Creates the driver the concurrent check uses: the one the sequential check uses unless a machine needs a store
    /// that tolerates calls under different ambient tenants at the same time.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="stores">The stores under test.</param>
    /// <param name="scope">The tenant namespace of the race.</param>
    /// <returns>A driver.</returns>
    public virtual IStepDriver<TStep> NewRaceDriver(TPlan plan, DifferentialStores stores, DifferentialScope scope)
        => NewDriver(plan, stores, scope);

    private async Task RaceOnceAsync(TPlan plan, int setupCount, int taskCount, DifferentialStores stores)
    {
        var all = RaceSteps(plan);
        setupCount = RaceSetupLength(plan) ?? setupCount;
        var setup = all.Take(setupCount).ToList();
        var racing = all.Skip(setupCount).Take(6).ToList();

        if (racing.Count < 2)
        {
            return;
        }

        var scope = DifferentialScope.Next();
        var driver = new DeadlockRerunDriver<TStep>(NewRaceDriver(plan, stores, scope));

        try
        {
            await RaceWithDriverAsync(plan, setup, racing, taskCount, stores, driver);
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

    private async Task RaceWithDriverAsync(TPlan plan, List<TStep> setup, List<TStep> racing, int taskCount, DifferentialStores stores, IStepDriver<TStep> driver)
    {
        var model = NewModel(plan, stores.Traits);
        var concreteSetup = new List<TStep>();

        // The setup runs one step at a time and must agree with the model, as in the sequential check.
        foreach (var step in setup)
        {
            var concrete = model.Concretize(step);
            concreteSetup.Add(concrete);
            var expected = model.Apply(concrete);
            var actual = await ApplyCatchingAsync(driver, concrete);
            var difference = Observation.Compare(expected, actual);

            if (difference.Length > 0)
            {
                throw new DifferentialMismatchException($"{Name} on {stores.Traits.Name}: setup step {Describe(concrete)} - {difference}.\n  expected: {expected}\n  actual:   {actual}");
            }
        }

        // What a step leaves to the current state is resolved once, against the state the tasks start from.
        var concreteRacing = racing.Select(model.Concretize).ToList();
        var lanes = Enumerable.Range(0, taskCount).Select(static _ => new List<int>()).ToList();

        for (var index = 0; index < concreteRacing.Count; index++)
        {
            lanes[index % taskCount].Add(index);
        }

        var observed = new Observation[concreteRacing.Count];
        var gate = new TaskCompletionSource();
        var running = lanes.Where(static lane => lane.Count > 0).Select(lane => Task.Run(async () =>
        {
            await gate.Task;

            foreach (var index in lane)
            {
                observed[index] = await ApplyCatchingAsync(driver, concreteRacing[index]);
            }
        })).ToList();

        gate.SetResult();
        await Task.WhenAll(running);

        var snapshot = new List<TStep>();
        var snapshotObserved = new List<Observation>();

        foreach (var step in Snapshot(plan))
        {
            snapshot.Add(step);
            snapshotObserved.Add(await ApplyCatchingAsync(driver, step));
        }

        // Every order of the steps that keeps each task's own order.
        foreach (var order in Interleavings([.. lanes.Select(static lane => lane.Count)]))
        {
            var candidate = NewModel(plan, stores.Traits);
            var matches = true;

            foreach (var step in concreteSetup)
            {
                candidate.Apply(step);
            }

            var lanePositions = new int[lanes.Count];

            foreach (var lane in order)
            {
                var index = lanes[lane][lanePositions[lane]++];

                if (Observation.Compare(candidate.Apply(concreteRacing[index]), observed[index]).Length > 0)
                {
                    matches = false;

                    break;
                }
            }

            for (var index = 0; matches && index < snapshot.Count; index++)
            {
                matches = Observation.Compare(candidate.Apply(snapshot[index]), snapshotObserved[index]).Length == 0;
            }

            if (matches)
            {
                return;
            }
        }

        var builder = new StringBuilder();
        builder.Append(Name).Append(" on ").Append(stores.Traits.Name).AppendLine(": no serial order of the model explains what the concurrent steps and the read-back returned.");

        for (var index = 0; index < concreteRacing.Count; index++)
        {
            builder.Append("  task ").Append(index % taskCount).Append(": ").AppendLine(Describe(concreteRacing[index]));
            builder.Append("    observed: ").AppendLine(observed[index].ToString());
        }

        for (var index = 0; index < snapshot.Count; index++)
        {
            builder.Append("  read-back: ").AppendLine(Describe(snapshot[index]));
            builder.Append("    observed: ").AppendLine(snapshotObserved[index].ToString());
        }

        throw new DifferentialMismatchException(builder.ToString());
    }
}
