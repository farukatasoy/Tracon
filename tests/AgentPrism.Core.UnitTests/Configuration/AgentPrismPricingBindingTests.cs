using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Configuration;

/// <summary>
/// MT-CORE-065: <c>AgentPrism:Pricing:{saglayici}:{model}</c> yalniz kisa
/// <c>Input</c>/<c>Output</c> anahtarlarini okur (K-021, elle baglama). C#
/// ozellik adiyla (<c>InputCostPerMillionTokens</c>) veya baska bir tiposla
/// yazilan bir fiyat girdisi K-034 geregi TAMAMEN SESSIZCE dusmemelidir.
/// </summary>
public sealed class AgentPrismPricingBindingTests
{
    [Fact]
    public void Yanlis_anahtar_adiyla_yazilan_fiyat_Providers_tan_dusurulmez()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Dogrusu "Input"/"Output"tur; bilerek C# ozellik adi yazildi.
            ["Pricing:echo:echo-1:InputCostPerMillionTokens"] = "0.25",
            ["Pricing:echo:echo-1:OutputCostPerMillionTokens"] = "1.0",
        };

        var options = BindOnly(configValues);

        // Model TAMAMEN kaybolmaz; ikisi de bos bir kayit olarak Providers'a
        // girer — dogrulayici bunu acilista reddeder (asagidaki test).
        options.Pricing.Providers.ShouldContainKey("echo");
        options.Pricing.Providers["echo"].ShouldContainKey("echo-1");
        options.Pricing.Providers["echo"]["echo-1"].InputCostPerMillionTokens.ShouldBeNull();
        options.Pricing.Providers["echo"]["echo-1"].OutputCostPerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public void Dogru_kisa_anahtarla_yazilan_fiyat_kabul_edilir()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Pricing:echo:echo-1:Input"] = "0.25",
            ["Pricing:echo:echo-1:Output"] = "1.0",
        };

        var options = BindOnly(configValues);

        options.Pricing.Providers["echo"]["echo-1"].InputCostPerMillionTokens.ShouldBe(0.25m);
        options.Pricing.Providers["echo"]["echo-1"].OutputCostPerMillionTokens.ShouldBe(1.0m);
    }

    [Fact]
    public void Yanlis_anahtar_adiyla_yazilan_fiyat_baslangicta_reddedilir()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AgentPrism:Pricing:echo:echo-1:InputCostPerMillionTokens"] = "0.25",
        };

        using var provider = BuildAgentPrism(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value)
            .Message.ShouldContain("echo:echo-1");
    }

    [Fact]
    public void Yanlis_anahtar_adiyla_yazilan_ses_fiyati_baslangicta_reddedilir()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Dogrusu "PerMillionCharacters"tir; bilerek yanlis yazildi.
            ["AgentPrism:Pricing:Voice:elevenlabs:tts-1:PerMillionChars"] = "30.0",
        };

        using var provider = BuildAgentPrism(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value)
            .Message.ShouldContain("Voice:elevenlabs:tts-1");
    }

    [Fact]
    public void Dogru_anahtarla_yazilan_fiyat_baslangicta_kabul_edilir()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AgentPrism:Pricing:echo:echo-1:Input"] = "0.25",
            ["AgentPrism:Pricing:echo:echo-1:Output"] = "1.0",
        };

        using var provider = BuildAgentPrism(configValues);

        var options = provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value;

        options.Pricing.Providers["echo"]["echo-1"].InputCostPerMillionTokens.ShouldBe(0.25m);
    }

    /// <summary>
    /// <c>AgentPrismServiceCollectionExtensions.Bind</c>'i (private, elle
    /// yazilan K-021 baglayicisi) DI/dogrulayici devreye girmeden dogrudan
    /// cagirir. Amac: bu testlerin yalniz BAGLAMA davranisini olcmesi — bir
    /// sonraki dogrulama testleri ayni senaryoyu acilis hatasi olarak dogrular.
    /// </summary>
    private static AgentPrismOptions BindOnly(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var options = new AgentPrismOptions();

        var bind = typeof(AgentPrismOptions).Assembly
            .GetType("AgentPrism.AgentPrismServiceCollectionExtensions")!
            .GetMethod(
                "Bind",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(IConfiguration), typeof(AgentPrismOptions)])
            ?? throw new InvalidOperationException("AgentPrismServiceCollectionExtensions.Bind bulunamadi.");

        bind.Invoke(null, [configuration, options]);

        return options;
    }

    private static ServiceProvider BuildAgentPrism(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddAgentPrism(configuration.GetSection(AgentPrismOptions.SectionName));

        return services.BuildServiceProvider();
    }
}
