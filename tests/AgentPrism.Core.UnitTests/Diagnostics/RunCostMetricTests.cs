using System.Diagnostics.Metrics;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// <c>agentprism.run.cost</c> sayacinin Faz 35 sozlesmesini dogrular: fiyat
/// tanimliyken yayilir, etiket kumesi kararlidir, agac toplamini ICERMEZ (K-151).
/// </summary>
/// <remarks>
/// Testler <see cref="RunRecordingAgent"/> uzerinden UCTAN UCA calisir, yalniz
/// <see cref="AgentPrismMetrics.RecordCost"/>'u yalitilmis cagirmaz — Faz 20'nin
/// dersi (K-157): bir parametre eklemek onu KULLANMAKLA ayni sey degildir,
/// aradaki katman atlanirsa birim testleri bunu yakalamaz.
/// </remarks>
public sealed class RunCostMetricTests
{
    [Fact]
    public async Task Fiyat_tanimliyken_maliyet_yayilir_ve_etiketler_kararlidir()
    {
        var store = new InMemoryRunStore();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        var usage = new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 500_000, TotalTokenCount = 1_500_000 };
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")) { Usage = usage });

        var provider = new FakeModelProvider(client, name: "fake", models:
        [
            new ModelDescriptor { Name = "priced-model", InputCostPerMillionTokens = 2m, OutputCostPerMillionTokens = 4m },
        ]);

        var resolver = new RunPricingResolver(
            new ModelProviderRegistry([provider]),
            Options.Create(new AgentPrismOptions { Pricing = new AgentPrismPricingOptions { Currency = "USD" } }));

        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("merhaba");

        // 2$/M girdi * 1.000.000 + 4$/M cikti * 500.000 = 2 + 2 = 4.
        collector.DoubleValues(AgentPrismDiagnostics.RunCostCounterName).ShouldBe([4.0]);

        var tags = collector.Tags(AgentPrismDiagnostics.RunCostCounterName).ShouldHaveSingleItem();
        tags.Count.ShouldBe(4);
        tags[AgentPrismDiagnostics.Tags.AgentName].ShouldBe("test-agent");
        tags[AgentPrismDiagnostics.Tags.ModelId].ShouldBe("priced-model");
        tags[AgentPrismDiagnostics.Tags.TenantId].ShouldBe("test");
        tags[AgentPrismDiagnostics.Tags.Currency].ShouldBe("USD");

        // 🚨 run.id ASLA etiket olmaz: her calistirma yeni bir zaman serisi acardi.
        tags.ShouldNotContainKey(AgentPrismDiagnostics.Tags.RunId);
    }

    [Fact]
    public async Task Fiyat_tanimsizsa_hicbir_maliyet_yayilmaz()
    {
        var store = new InMemoryRunStore();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 10, TotalTokenCount = 20 };
        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")) { Usage = usage });

        var provider = new FakeModelProvider(client, name: "fake", models: [new ModelDescriptor { Name = "unpriced-model" }]);
        var resolver = new RunPricingResolver(new ModelProviderRegistry([provider]), Options.Create(new AgentPrismOptions()));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "unpriced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "unpriced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        await agent.RunAsync("merhaba");

        // Bilinmeyen fiyati sifir olarak yaymak gercek harcamayi kucuk gosterirdi;
        // hicbir olcum uretilmemesi dogru davranistir.
        collector.DoubleValues(AgentPrismDiagnostics.RunCostCounterName).ShouldBeEmpty();
    }

    /// <summary>
    /// 🚨 K-151 regresyon korumasi: kok + iki alt calistirmada sayac
    /// yalnizca UC bagimsiz olcum uretir ve toplami agac toplamini DEGIL,
    /// uc calistirmanin KENDI maliyetlerinin toplamini verir.
    /// </summary>
    [Fact]
    public async Task Kok_ve_iki_alt_calistirmada_sayac_agac_toplamini_cift_saymaz()
    {
        var store = new InMemoryRunStore();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        // Cagri sirasina gore azalan token: kok en cok, alt calistirmalar daha az
        // harcar. Fiyat 2$/M girdi, cikti yok.
        var usagesByCall = new Queue<UsageDetails>(
        [
            new UsageDetails { InputTokenCount = 1_000_000, OutputTokenCount = 0, TotalTokenCount = 1_000_000 },
            new UsageDetails { InputTokenCount = 500_000, OutputTokenCount = 0, TotalTokenCount = 500_000 },
            new UsageDetails { InputTokenCount = 250_000, OutputTokenCount = 0, TotalTokenCount = 250_000 },
        ]);

        var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")) { Usage = usagesByCall.Dequeue() });

        var provider = new FakeModelProvider(client, name: "fake", models:
        [
            new ModelDescriptor { Name = "priced-model", InputCostPerMillionTokens = 2m },
        ]);

        var resolver = new RunPricingResolver(new ModelProviderRegistry([provider]), Options.Create(new AgentPrismOptions()));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());
        var definition = TestData.Definition() with { Model = new ModelBinding { Provider = "fake", Model = "priced-model" } };

        var agent = new RunRecordingAgent(
            compiler.Compile(definition),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            metrics: metrics,
            modelId: "priced-model",
            modelProvider: "fake",
            pricingResolver: resolver);

        var rootRunId = AgentPrismId.NewId();

        // Kok: 2$/M * 1.000.000 = 2.0
        await agent.RunAsync("kok istegi", options: new AgentPrismRunOptions { RunId = rootRunId });

        // Alt 1: 2$/M * 500.000 = 1.0
        await agent.RunAsync(
            "alt istegi 1",
            options: new AgentPrismRunOptions { ParentRunId = rootRunId, RootRunId = rootRunId, Depth = 1 });

        // Alt 2: 2$/M * 250.000 = 0.5
        await agent.RunAsync(
            "alt istegi 2",
            options: new AgentPrismRunOptions { ParentRunId = rootRunId, RootRunId = rootRunId, Depth = 1 });

        var costs = collector.DoubleValues(AgentPrismDiagnostics.RunCostCounterName);

        costs.Count.ShouldBe(3);
        costs.ShouldBe([2.0, 1.0, 0.5], ignoreOrder: true);

        // Toplam UC calistirmanin KENDI maliyetidir; agac toplami (kok +
        // altlarin agac toplami tekrar sayilmis hali) OLMAMALIDIR.
        costs.Sum().ShouldBe(3.5);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>
    /// Testin kendi <see cref="AgentPrismMetrics"/> ornegine ozel, tekil kimlikli
    /// bir <see cref="Meter"/> uretir.
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="MeterListener"/> sureç genelinde calisir: isme gore filtreleme
    /// paralel kosan baska bir test sinifinin AYNI isimli ama FARKLI <see cref="Meter"/>
    /// orneginin olcumlerini de yakalar (bkz. <c>ObservabilityTests.TestMeterFactory</c>).
    /// </remarks>
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Meter { get; } = new(AgentPrismDiagnostics.MeterName);

        public Meter Create(MeterOptions options) => Meter;

        public void Dispose() => Meter.Dispose();
    }

    /// <summary>
    /// Belirli bir <c>Meter</c> <strong>orneginin</strong> olcumlerini toplayan
    /// basit dinleyici. Filtre isme degil, referansa gore yapilir.
    /// </summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, Dictionary<string, object?> Tags)> _doubles = [];
        private readonly Lock _gate = new();

        public MetricCollector(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _doubles.Add((instrument.Name, value, ToDictionary(tags)));
                }
            });

            _listener.Start();
        }

        public List<double> DoubleValues(string name)
        {
            lock (_gate)
            {
                return [.. _doubles.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Value)];
            }
        }

        public List<Dictionary<string, object?>> Tags(string name)
        {
            lock (_gate)
            {
                return [.. _doubles.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Tags)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                result[tag.Key] = tag.Value;
            }

            return result;
        }
    }
}
