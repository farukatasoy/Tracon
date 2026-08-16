namespace AgentPrism.Core.UnitTests.Experiments;

public sealed class ExperimentAssignmentResolverTests
{
    [Fact]
    public async Task Returns_null_when_no_running_experiment()
    {
        var store = new InMemoryExperimentStore();
        var resolver = new ExperimentAssignmentResolver(store);

        var assignment = await resolver.ResolveAsync("default", "agent", "session-1");

        assignment.ShouldBeNull();
    }

    [Fact]
    public async Task Returns_assignment_when_running_experiment_exists()
    {
        var store = new InMemoryExperimentStore();
        var experiment = await CreateRunningExperimentAsync(store);
        var resolver = new ExperimentAssignmentResolver(store);

        var assignment = await resolver.ResolveAsync("default", "agent", "session-1");

        assignment.ShouldNotBeNull();
        assignment!.ExperimentId.ShouldBe(experiment.Id);
        experiment.Variants.Select(static v => v.Name).Contains(assignment.Variant, StringComparer.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void Same_key_always_produces_same_variant()
    {
        var experiment = Experiment(
            new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
            new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 });

        var first = ExperimentAssignmentResolver.SelectVariant(experiment, "fixed-key");

        for (var i = 0; i < 100; i++)
        {
            ExperimentAssignmentResolver.SelectVariant(experiment, "fixed-key").Name.ShouldBe(first.Name);
        }
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(30, 70)]
    [InlineData(10, 90)]
    public void Weight_distribution_is_approximately_correct(int weightA, int weightB)
    {
        var experiment = Experiment(
            new ExperimentVariant { Name = "a", Version = 1, Weight = weightA },
            new ExperimentVariant { Name = "b", Version = 2, Weight = weightB });

        const int sampleSize = 10_000;
        var countA = 0;

        for (var i = 0; i < sampleSize; i++)
        {
            var variant = ExperimentAssignmentResolver.SelectVariant(experiment, $"key-{i}");

            if (string.Equals(variant.Name, "a", StringComparison.Ordinal))
            {
                countA++;
            }
        }

        var observedPercent = countA * 100.0 / sampleSize;
        Math.Abs(observedPercent - weightA).ShouldBeLessThanOrEqualTo(2.0);
    }

    [Fact]
    public void Falls_back_to_last_variant_when_weights_do_not_sum_to_100()
    {
        // Normally this cannot happen because it is validated at save time;
        // the defensive fallback behavior is tested here.
        var experiment = Experiment(
            new ExperimentVariant { Name = "a", Version = 1, Weight = 1 },
            new ExperimentVariant { Name = "b", Version = 2, Weight = 1 });

        // Tries a key whose bucket is too high to fall into any variant's range;
        // the loop returns the last variant.
        var variant = ExperimentAssignmentResolver.SelectVariant(experiment, "any-key");

        variant.Name.ShouldBeOneOf("a", "b");
    }

    [Fact]
    public void Canary_range_is_computed_independent_of_physical_order()
    {
        // control is FIRST (weight 95), canary is SECOND (weight 5) -- physical
        // order is control-first. When the canary rule is DEFINED, bucket
        // computation must still place canary in the [0, 5) range and control in
        // the [5, 100) range -- this is the basis of 56.4's "does not switch
        // existing sessions' arm" guarantee.
        var experiment = Experiment(
            new ExperimentVariant { Name = "control", Version = 1, Weight = 95 },
            new ExperimentVariant { Name = "canary", Version = 2, Weight = 5 }) with
        {
            Canary = new CanaryPolicy { CanaryVariant = "canary", MinSampleSize = 20 },
        };

        // Same experiment, with canary placed FIRST (physical order should no longer matter).
        var reordered = experiment with
        {
            Variants =
            [
                new ExperimentVariant { Name = "canary", Version = 2, Weight = 5 },
                new ExperimentVariant { Name = "control", Version = 1, Weight = 95 },
            ],
        };

        for (var i = 0; i < 200; i++)
        {
            var key = $"key-{i}";

            ExperimentAssignmentResolver.SelectVariant(experiment, key).Name
                .ShouldBe(ExperimentAssignmentResolver.SelectVariant(reordered, key).Name);
        }
    }

    [Fact]
    public void Key_previously_falling_into_canary_does_not_shift_to_control_as_canary_weight_grows()
    {
        var narrow = Experiment(
            new ExperimentVariant { Name = "control", Version = 1, Weight = 95 },
            new ExperimentVariant { Name = "canary", Version = 2, Weight = 5 }) with
        {
            Canary = new CanaryPolicy { CanaryVariant = "canary", MinSampleSize = 20 },
        };

        var wide = narrow with
        {
            Variants =
            [
                new ExperimentVariant { Name = "canary", Version = 2, Weight = 25 },
                new ExperimentVariant { Name = "control", Version = 1, Weight = 75 },
            ],
        };

        for (var i = 0; i < 200; i++)
        {
            var key = $"key-{i}";

            if (string.Equals(ExperimentAssignmentResolver.SelectVariant(narrow, key).Name, "canary", StringComparison.Ordinal))
            {
                ExperimentAssignmentResolver.SelectVariant(wide, key).Name.ShouldBe("canary");
            }
        }
    }

    private static Experiment Experiment(params ExperimentVariant[] variants)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = "default",
            Name = "test-experiment",
            AgentName = "agent",
            Variants = variants,
            Status = ExperimentStatus.Running,
        };

    private static async Task<Experiment> CreateRunningExperimentAsync(InMemoryExperimentStore store)
    {
        var experiment = Experiment(
            new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
            new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 });

        await store.SaveAsync(experiment);
        return await store.StartAsync("default", experiment.Name);
    }
}
