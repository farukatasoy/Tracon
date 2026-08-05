using AgentPrism.Azure.UnitTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Azure.UnitTests;

/// <summary>
/// <c>UseAzureOpenAI()</c> kaydinin sekli, tekrarlanmasi ve yapilandirmadan
/// baglanmasi.
/// </summary>
public sealed class AzureOpenAIProviderExtensionsTests
{
    [Fact]
    public void Tek_saglayici_kaydeder()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey));

        var names = provider.GetServices<IModelProvider>().Select(static p => p.Name).ToList();

        names.ShouldBe([AzureOpenAIProviderNames.AzureOpenAI]);
    }

    [Fact]
    public void Ikinci_cagri_saglayiciyi_cogaltmaz()
    {
        using var provider = Build(builder => builder
            .UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey)
            .UseAzureOpenAI(options => options.DefaultDeployment = TestData.Deployment));

        provider.GetServices<IModelProvider>().Count().ShouldBe(1);

        // Ikinci cagri ayarlari birlestirir; defter "ayni ad iki kez" hatasi vermez.
        provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value.DefaultDeployment
            .ShouldBe(TestData.Deployment);
        provider.GetRequiredService<IModelProviderRegistry>().List().Count.ShouldBe(1);
    }

    [Fact]
    public void Tek_fabrika_paylasilir()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey));

        // Tek istemci, tek HTTP baglanti havuzu.
        provider.GetRequiredService<AzureOpenAIChatClientFactory>()
            .ShouldBeSameAs(provider.GetRequiredService<AzureOpenAIChatClientFactory>());
    }

    [Fact]
    public void Yapilandirmadan_okur()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(Configuration()));

        var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

        options.Endpoint.ShouldBe(TestData.Endpoint);
        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.DefaultDeployment.ShouldBe(TestData.Deployment);
        options.Audience.ShouldBe("https://cognitiveservices.azure.us/.default");
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.Models.Single().Name.ShouldBe(TestData.Deployment);
        options.Models.Single().ContextWindowTokens.ShouldBe(128000);
        options.Models.Single().InputCostPerMillionTokens.ShouldBe(0.15m);
    }

    [Fact]
    public void Yapilandirma_okunduktan_sonra_kimlik_fabrikasi_kodda_verilebilir()
    {
        // CredentialFactory bir delegate'tir ve yapilandirmadan okunamaz; bu yuzden
        // yapilandirma asiri yuklemesi bir ek degistirici kabul eder.
        var credential = new SahteTokenKimligi();

        using var provider = Build(builder => builder.UseAzureOpenAI(
            Configuration(),
            options => options.CredentialFactory = () => credential));

        var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

        options.CredentialFactory.ShouldNotBeNull();
        options.CredentialFactory().ShouldBeSameAs(credential);
        options.ApiKey.ShouldBe(TestData.ApiKey);
    }

    [Fact]
    public void Adressiz_kayit_baslangicta_hata_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseAzureOpenAI(options => options.ApiKey = TestData.ApiKey);

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Kimliksiz_kayit_baslangicta_hata_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseAzureOpenAI(options => options.Endpoint = TestData.Endpoint);

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value);

        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.ApiKey));
        exception.Message.ShouldContain(nameof(AzureOpenAIProviderOptions.CredentialFactory));
    }

    [Fact]
    public void Anahtar_yerine_kimlik_fabrikasi_yeterlidir()
    {
        // Yonetilen kimlik yolunda API anahtari HIC yoktur; dogrulama bunu
        // eksik ayar saymamalidir.
        var services = new ServiceCollection();
        services.AddAgentPrism().UseAzureOpenAI(options =>
        {
            options.Endpoint = TestData.Endpoint;
            options.CredentialFactory = static () => new SahteTokenKimligi();
        });

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureOpenAIProviderOptions>>().Value;

        options.ApiKey.ShouldBeNull();
        provider.GetServices<IModelProvider>().Single().Name.ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);
    }

    [Fact]
    public void Bos_anahtar_argumani_reddedilir()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(
            () => services.AddAgentPrism().UseAzureOpenAI(TestData.Endpoint, "   "));
    }

    [Fact]
    public void Katalog_yapilandirmadan_gelir_ve_saglayiciya_yansir()
    {
        using var provider = Build(builder => builder.UseAzureOpenAI(TestData.Endpoint, TestData.ApiKey, options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "deneme-gpt" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment });
        }));

        var descriptor = provider.GetRequiredService<IModelProviderRegistry>().List().Single();

        descriptor.Name.ShouldBe(AzureOpenAIProviderNames.AzureOpenAI);
        descriptor.Models.Select(static m => m.Name).ShouldBe(["deneme-gpt", TestData.Deployment]);
    }

    private static IConfiguration Configuration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Endpoint"] = TestData.EndpointText,
                ["ApiKey"] = TestData.ApiKey,
                ["DefaultDeployment"] = TestData.Deployment,
                ["Audience"] = "https://cognitiveservices.azure.us/.default",
                ["Timeout"] = "00:00:30",
                ["Models:0:Name"] = TestData.Deployment,
                ["Models:0:ContextWindowTokens"] = "128000",
                ["Models:0:InputCostPerMillionTokens"] = "0.15",
            })
            .Build();

    private static ServiceProvider Build(Action<IAgentPrismBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddAgentPrism());

        return services.BuildServiceProvider();
    }
}
