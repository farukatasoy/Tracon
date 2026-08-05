using AgentPrism.Azure.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;

namespace AgentPrism.Azure.UnitTests;

/// <summary>
/// Fabrikanin deployment cozumu, kimlik secimi, boru hatti kurulumu ve saglayici
/// ayarlarini reddetmesi.
/// </summary>
public sealed class AzureOpenAIChatClientFactoryTests
{
    [Fact]
    public void Boru_hatti_tool_dongusu_ve_telemetri_icerir()
    {
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        // Tool dongusu MAF'a birakilir; span'ler Faz 6'nin kaynagi altinda uretilir.
        chatClient.GetService(typeof(FunctionInvokingChatClient)).ShouldNotBeNull();
        chatClient.GetService(typeof(OpenTelemetryChatClient)).ShouldNotBeNull();
    }

    [Fact]
    public void Deployment_adi_istemci_ustverisine_model_olarak_yansir()
    {
        // Azure'da yol {endpoint}/openai/deployments/{deployment}/chat/completions
        // seklindedir; MEAI ustverisinde bu ad "model" alanini doldurur.
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Deployment);
        metadata.ProviderUri!.ToString().ShouldStartWith(TestData.EndpointText);
    }

    [Fact]
    public void Deployment_bos_ise_varsayilan_deployment_kullanilir()
    {
        using var chatClient = Factory(options => options.DefaultDeployment = TestData.Deployment)
            .CreateChatClient(new ModelBinding { Provider = AzureOpenAIProviderNames.AzureOpenAI, Model = "  " });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();

        metadata.DefaultModelId.ShouldBe(TestData.Deployment);
    }

    [Fact]
    public void Deployment_ve_varsayilani_yoksa_hata_model_degil_deployment_bekledigini_soyler()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory()
            .CreateChatClient(new ModelBinding { Provider = AzureOpenAIProviderNames.AzureOpenAI, Model = " " }));

        // Fazin tasidigi tek buyuk kavram karisikligi budur; mesaj bunu acikca
        // soylemezse kullanici "model bulunamadi" sanip yanlis yerde arar.
        exception.Message.ShouldContain("DEPLOYMENT");
        exception.Message.ShouldContain(nameof(ModelBinding.Model));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.DefaultDeployment));
    }

    [Fact]
    public void Adressiz_istemci_kurulumu_hata_verir()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => AzureOpenAIChatClientFactory.CreateClient(new AzureOpenAIProviderOptions { ApiKey = TestData.ApiKey }));

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Kimliksiz_istemci_kurulumu_iki_yolu_da_anlatir()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => AzureOpenAIChatClientFactory.CreateClient(new AzureOpenAIProviderOptions { Endpoint = TestData.Endpoint }));

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.ApiKey));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void Kimlik_fabrikasi_kurulum_sirasinda_bir_kez_cagrilir()
    {
        var credential = new SahteTokenKimligi();
        var cagriSayisi = 0;

        var factory = new AzureOpenAIChatClientFactory(TestData.Options(o =>
        {
            o.ApiKey = null;
            o.CredentialFactory = () => { cagriSayisi++; return credential; };
        }));

        using var first = factory.CreateChatClient(TestData.Binding());
        using var second = factory.CreateChatClient(TestData.Binding("baska-deployment"));

        // Istemci bir kez kurulur; her derlemede yeni kimlik uretmek token
        // onbellegini bosa cikarirdi.
        cagriSayisi.ShouldBe(1);
    }

    [Fact]
    public void Kimlik_fabrikasi_anahtari_ezer()
    {
        // Ikisi de verildiginde daha guvenli olan kazanir; sessizce anahtara
        // dusmek kullanicinin bekledigi davranis degildir.
        var credential = new SahteTokenKimligi();
        var kullanildi = false;

        var client = AzureOpenAIChatClientFactory.CreateClient(TestData.Options(o =>
            o.CredentialFactory = () => { kullanildi = true; return credential; }));

        kullanildi.ShouldBeTrue();
        client.ShouldNotBeNull();
    }

    [Fact]
    public void Kimlik_fabrikasi_null_dondururse_anlasilir_hata_verir()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => AzureOpenAIChatClientFactory.CreateClient(TestData.Options(o =>
            {
                o.ApiKey = null;
                o.CredentialFactory = static () => null!;
            })));

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void Egemen_bulut_kapsami_istemciye_verilebilir()
    {
        // Kurulum hata vermemelidir; kapsam SDK'nin ic ayarina gider ve disaridan
        // okunamaz. Saglik denetimi tarafindaki karsiligi ayri test edilir.
        var client = AzureOpenAIChatClientFactory.CreateClient(TestData.Options(o =>
            o.Audience = "https://cognitiveservices.azure.us/.default"));

        client.ShouldNotBeNull();
    }

    [Fact]
    public void Hicbir_saglayici_ayari_desteklenmez()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("azure-openai.yokBoyleAyar", true)))));

        exception.Message.ShouldContain("taninmiyor");
        exception.Message.ShouldContain("hicbir ek ayar desteklemiyor");
    }

    [Fact]
    public void Baska_saglayiciya_ait_ayar_ayri_bir_hata_mesaji_verir()
    {
        var exception = Should.Throw<AgentPrismException>(() => Factory().CreateChatClient(
            TestData.Binding(providerSettings: TestData.Settings(("anthropic.promptCaching", true)))));

        // Yanlis onek "ayari baska bir saglayiciya yazdin" demektir; duzeltmesi
        // taninmayan anahtardan farklidir.
        exception.Message.ShouldContain("ait degil");
        exception.Message.ShouldContain("anthropic.promptCaching");
    }

    [Fact]
    public void Ayarsiz_baglanti_sorunsuz_calisir()
    {
        using var chatClient = Factory().CreateChatClient(TestData.Binding());

        chatClient.ShouldNotBeNull();
    }

    private static AzureOpenAIChatClientFactory Factory(Action<AzureOpenAIProviderOptions>? configure = null)
        => new(TestData.Options(configure));
}
