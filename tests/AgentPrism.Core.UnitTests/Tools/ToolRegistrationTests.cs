using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Tools;

/// <summary>
/// Tool defterinin arayuze verdigi tanimlari ve <c>AddToolsFrom&lt;T&gt;()</c>
/// taramasini dogrular.
/// </summary>
/// <remarks>
/// Tool'lar yalnizca kodda tanimlanir; arayuz bu listeden secim yaptirir.
/// Bu bir guvenlik sinirdir. Gerekce: <c>docs/MIMARI.md</c>, kural K2.
/// </remarks>
public sealed class ToolRegistrationTests
{
    [Fact]
    public void Tool_tanimi_ad_aciklama_ve_json_semasi_tasir()
    {
        var registry = TestData.Registry(TestData.Tool("get_order", "Siparis durumunu dondurur"));

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe("get_order");
        descriptor.Description.ShouldBe("Siparis durumunu dondurur");
        descriptor.JsonSchema.ShouldNotBeNullOrWhiteSpace();
        JsonDocument.Parse(descriptor.JsonSchema!).RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public void Json_semasi_parametreleri_icerir()
    {
        var registry = TestData.Registry(
            Microsoft.Extensions.AI.AIFunctionFactory.Create(
                (string orderId) => $"durum: {orderId}",
                "get_order_status",
                "Kargo durumunu dondurur"));

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.JsonSchema!.ShouldContain("orderId");
    }

    [Fact]
    public void Onay_gereksinimi_tanima_tasinir()
    {
        var registry = new ToolRegistry([new AgentPrismToolRegistration(TestData.Tool("sil"), requiresApproval: true)]);

        registry.List().ShouldHaveSingleItem().RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Ayni_ad_iki_kez_kaydedilirse_hata_verilir()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => TestData.Registry(TestData.Tool("ayni"), TestData.Tool("ayni")));

        exception.Message.ShouldContain("ayni");
    }

    [Fact]
    public void AddToolsFrom_yalnizca_isaretli_metotlari_kaydeder()
    {
        var registry = BuildRegistry(builder => builder.AddToolsFrom(typeof(OrnekToolSinifi)));

        registry.List().Select(static descriptor => descriptor.Name)
            .ShouldBe(["VarsayilanAdliMetot", "ozel_ad"], ignoreOrder: true);
    }

    [Fact]
    public void AddToolsFrom_aciklama_ve_onay_bilgisini_tasir()
    {
        var registry = BuildRegistry(builder => builder.AddToolsFrom(typeof(OrnekToolSinifi)));

        var descriptor = registry.List().Single(static d => string.Equals(d.Name, "ozel_ad", StringComparison.Ordinal));

        descriptor.Description.ShouldBe("Ozel adli tool.");
        descriptor.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void AddToolsFrom_ad_verilmezse_metot_adini_kullanir()
    {
        var registry = BuildRegistry(builder => builder.AddToolsFrom(typeof(OrnekToolSinifi)));

        registry.TryGet("VarsayilanAdliMetot", out _).ShouldBeTrue();
    }

    [Fact]
    public void AddToolsFrom_isaretli_metot_yoksa_hata_verir()
    {
        // Sessiz bir bos kayit, kullaniciya tool'lari kaydettigini dusundururdu.
        var exception = Should.Throw<AgentPrismException>(
            () => BuildRegistry(builder => builder.AddToolsFrom(typeof(IsaretsizSinif))));

        exception.Message.ShouldContain(nameof(IsaretsizSinif));
        exception.Message.ShouldContain("AgentPrismTool");
    }

    [Fact]
    public async Task AddToolsFrom_ornek_metodunu_servis_saglayicidan_cozer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SelamlamaAyari("Merhaba"));
        services.AddAgentPrism().AddToolsFrom<OrnekMetotluSinif>();

        await using var provider = services.BuildServiceProvider();

        var tool = provider.GetRequiredService<IToolRegistry>();
        tool.TryGet("selamla", out var function).ShouldBeTrue();

        var result = await function!.InvokeAsync(
            new Microsoft.Extensions.AI.AIFunctionArguments(StringComparer.Ordinal)
            {
                ["ad"] = "Faruk",
                Services = provider,
            });

        (result?.ToString() ?? string.Empty).ShouldContain("Merhaba Faruk");
    }

    private static IToolRegistry BuildRegistry(Action<IAgentPrismBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddAgentPrism());

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IToolRegistry>();
    }

    private sealed record SelamlamaAyari(string Onek);

    private static class OrnekToolSinifi
    {
        [AgentPrismTool]
        public static string VarsayilanAdliMetot() => "sonuc";

        [AgentPrismTool("ozel_ad", "Ozel adli tool.", RequiresApproval = true)]
        public static string OzelAdliMetot() => "sonuc";

        public static string IsaretsizMetot() => "kaydedilmez";
    }

    private static class IsaretsizSinif
    {
        public static string HicbirSey() => "kaydedilmez";
    }

    private sealed class OrnekMetotluSinif(SelamlamaAyari ayar)
    {
        [AgentPrismTool("selamla")]
        public string Selamla(string ad) => $"{ayar.Onek} {ad}";
    }
}
