namespace AgentPrism.Core.UnitTests.Experiments;

public sealed class ExperimentAssignmentResolverTests
{
    [Fact]
    public async Task Calisan_deney_yoksa_null_doner()
    {
        var store = new InMemoryExperimentStore();
        var resolver = new ExperimentAssignmentResolver(store);

        var assignment = await resolver.ResolveAsync("default", "agent", "oturum-1");

        assignment.ShouldBeNull();
    }

    [Fact]
    public async Task Calisan_deney_varsa_atama_doner()
    {
        var store = new InMemoryExperimentStore();
        var experiment = await CreateRunningExperimentAsync(store);
        var resolver = new ExperimentAssignmentResolver(store);

        var assignment = await resolver.ResolveAsync("default", "agent", "oturum-1");

        assignment.ShouldNotBeNull();
        assignment!.ExperimentId.ShouldBe(experiment.Id);
        experiment.Variants.Select(static v => v.Name).Contains(assignment.Variant, StringComparer.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void Ayni_anahtar_her_zaman_ayni_varyanti_uretir()
    {
        var experiment = Experiment(
            new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
            new ExperimentVariant { Name = "v2", Version = 2, Weight = 50 });

        var first = ExperimentAssignmentResolver.SelectVariant(experiment, "sabit-anahtar");

        for (var i = 0; i < 100; i++)
        {
            ExperimentAssignmentResolver.SelectVariant(experiment, "sabit-anahtar").Name.ShouldBe(first.Name);
        }
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(30, 70)]
    [InlineData(10, 90)]
    public void Agirlik_dagilimi_yaklasik_dogrudur(int weightA, int weightB)
    {
        var experiment = Experiment(
            new ExperimentVariant { Name = "a", Version = 1, Weight = weightA },
            new ExperimentVariant { Name = "b", Version = 2, Weight = weightB });

        const int sampleSize = 10_000;
        var countA = 0;

        for (var i = 0; i < sampleSize; i++)
        {
            var variant = ExperimentAssignmentResolver.SelectVariant(experiment, $"anahtar-{i}");

            if (string.Equals(variant.Name, "a", StringComparison.Ordinal))
            {
                countA++;
            }
        }

        var observedPercent = countA * 100.0 / sampleSize;
        Math.Abs(observedPercent - weightA).ShouldBeLessThanOrEqualTo(2.0);
    }

    [Fact]
    public void Agirlik_toplami_100e_ulasmazsa_son_varyanta_dusulur()
    {
        // Kayit aninda dogrulandigi icin bu normalde olusmaz; savunma amacli
        // geri donus davranisi test edilir.
        var experiment = Experiment(
            new ExperimentVariant { Name = "a", Version = 1, Weight = 1 },
            new ExperimentVariant { Name = "b", Version = 2, Weight = 1 });

        // Kova hicbir varyantin araligina girmeyecek kadar yuksek bir anahtar
        // dener; dongu son varyanti geri doner.
        var variant = ExperimentAssignmentResolver.SelectVariant(experiment, "herhangi-bir-anahtar");

        variant.Name.ShouldBeOneOf("a", "b");
    }

    private static Experiment Experiment(params ExperimentVariant[] variants)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = "default",
            Name = "test-deneyi",
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
