using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>Saglayici defterinin ada gore cozum davranisini dogrular.</summary>
public sealed class ModelProviderRegistryTests
{
    [Fact]
    public void Saglayici_ada_gore_cozulur()
    {
        var provider = new FakeModelProvider(name: "birinci");
        var registry = new ModelProviderRegistry([provider, new FakeModelProvider(name: "ikinci")]);

        registry.CreateChatClient(TestData.Binding(provider: "birinci"));

        provider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Saglayici_adi_buyuk_kucuk_harfe_duyarsizdir()
    {
        var provider = new FakeModelProvider(name: "openai");
        var registry = new ModelProviderRegistry([provider]);

        registry.CreateChatClient(TestData.Binding(provider: "OpenAI"));

        provider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Bilinmeyen_saglayici_anlasilir_hata_verir()
    {
        var registry = new ModelProviderRegistry([new FakeModelProvider(name: "fake")]);

        var exception = Should.Throw<AgentPrismException>(
            () => registry.CreateChatClient(TestData.Binding(provider: "yok-boyle")));

        exception.Message.ShouldContain("yok-boyle");
        // Hata mesaji kayitli saglayicilari ve ne yapilacagini soylemeli.
        exception.Message.ShouldContain("fake");
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Hic_saglayici_yoksa_hata_bunu_soyler()
    {
        var registry = new ModelProviderRegistry([]);

        var exception = Should.Throw<AgentPrismException>(
            () => registry.CreateChatClient(TestData.Binding()));

        exception.Message.ShouldContain("hic saglayici kayitli degil");
    }

    [Fact]
    public void Ayni_ad_iki_kez_kaydedilirse_hata_verilir()
    {
        var exception = Should.Throw<AgentPrismException>(() => new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai"), new FakeModelProvider(name: "OPENAI")]));

        exception.Message.ShouldContain("openai");
    }

    [Fact]
    public void Liste_ada_gore_siralidir()
    {
        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(name: "zeta"), new FakeModelProvider(name: "alfa")]);

        registry.List().Select(static descriptor => descriptor.Name).ShouldBe(["alfa", "zeta"]);
    }

    [Fact]
    public void Liste_saglayicinin_modellerini_tasir()
    {
        var registry = new ModelProviderRegistry([new FakeModelProvider()]);

        registry.List().ShouldHaveSingleItem().Models.ShouldHaveSingleItem().Name.ShouldBe("fake-model");
    }

    [Fact]
    public void Null_baglanti_reddedilir()
    {
        var registry = new ModelProviderRegistry([new FakeModelProvider()]);

        Should.Throw<ArgumentNullException>(() => registry.CreateChatClient(null!));
    }
}
