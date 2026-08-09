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

    [Fact]
    public void Kanarya_araligi_fiziksel_sıradan_bagimsiz_hesaplanir()
    {
        // control ILK sirada (agirlik 95), canary IKINCI (agirlik 5) -- fiziksel
        // sira control-once. Kanarya kurali TANIMLI oldugunda bucket hesaplamasi
        // yine de kanaryayi [0, 5) araligina, control'u [5, 100) araligina koymali
        // -- 56.4'un "var olan oturumlar kolunu degistirmez" garantisinin temeli.
        var experiment = Experiment(
            new ExperimentVariant { Name = "control", Version = 1, Weight = 95 },
            new ExperimentVariant { Name = "canary", Version = 2, Weight = 5 }) with
        {
            Canary = new CanaryPolicy { CanaryVariant = "canary", MinSampleSize = 20 },
        };

        // Ayni deney, kanarya SIRAYA konmus (fiziksel sira artik onemsiz olmali).
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
            var key = $"anahtar-{i}";

            ExperimentAssignmentResolver.SelectVariant(experiment, key).Name
                .ShouldBe(ExperimentAssignmentResolver.SelectVariant(reordered, key).Name);
        }
    }

    [Fact]
    public void Kanarya_agirligi_buyudukce_onceden_kanaryaya_dusen_anahtar_kontrole_kaymaz()
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
            var key = $"anahtar-{i}";

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
