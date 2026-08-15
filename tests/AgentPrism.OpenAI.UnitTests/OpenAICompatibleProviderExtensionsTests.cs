using AgentPrism.OpenAI.UnitTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// <c>UseOpenAICompatible()</c> cagrisinin ad dogrulamasini, anahtarsiz yerel
/// saglayici davranisini ve saglayici kaydini dogrular.
/// </summary>
public sealed class OpenAICompatibleProviderExtensionsTests
{
    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.example.com/api/v1");
    private static readonly Uri LocalEndpoint = new("http://localhost:11434/v1");

    [Fact]
    public void Adlandirilmis_saglayici_defterde_gorunur()
    {
        using var provider = BuildProvider(o => o.Endpoint = OpenRouterEndpoint);

        var names = provider.GetRequiredService<IModelProviderRegistry>()
            .List().Select(static descriptor => descriptor.Name).ToList();

        names.ShouldContain(static name => string.Equals(name, "openrouter", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OpenAIProviderNames.ChatCompletions)]
    [InlineData(OpenAIProviderNames.Responses)]
    [InlineData("OPENAI")]
    public void Rezerve_ad_reddedilir(string name)
    {
        var builder = new ServiceCollection().AddAgentPrism();

        var exception = Should.Throw<ArgumentException>(
            () => builder.UseOpenAICompatible(name, o => o.Endpoint = OpenRouterEndpoint));

        exception.Message.ShouldContain("reserved");
    }

    [Theory]
    [InlineData("Openrouter")] // buyuk harf
    [InlineData("open router")] // bosluk
    [InlineData("-openrouter")] // tire ile baslar
    [InlineData("open_router")] // alt cizgi
    [InlineData("")]
    public void Gecersiz_ad_deseni_reddedilir(string name)
    {
        var builder = new ServiceCollection().AddAgentPrism();

        Should.Throw<ArgumentException>(
            () => builder.UseOpenAICompatible(name, o => o.Endpoint = OpenRouterEndpoint));
    }

    [Fact]
    public void Otuz_iki_karakterlik_ad_kabul_edilir_otuz_ucuncu_reddedilir()
    {
        var builder = new ServiceCollection().AddAgentPrism();
        var name32 = new string('a', 32);
        var name33 = new string('a', 33);

        Should.NotThrow(() => builder.UseOpenAICompatible(name32, o => o.Endpoint = OpenRouterEndpoint));
        Should.Throw<ArgumentException>(
            () => builder.UseOpenAICompatible(name33, o => o.Endpoint = OpenRouterEndpoint));
    }

    [Fact]
    public void Endpoint_verilmeyen_adlandirilmis_saglayici_dogrulama_hatasi_verir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAICompatible("openrouter", static _ => { });

        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(() => provider
            .GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>()
            .Get("openrouter"));

        exception.Message.ShouldContain(nameof(OpenAIProviderOptions.Endpoint));
    }

    [Fact]
    public void Anahtarsiz_yerel_saglayici_dogrulamayi_gecer_ve_istemci_uretir()
    {
        // F-05: yerel sunucular (Ollama, LM Studio) API anahtari istemez.
        using var provider = BuildProvider(o => o.Endpoint = LocalEndpoint, name: "ollama");

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "ollama", Model = "llama3.1" });

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Anahtarsiz_saglayicinin_uretilen_istemcisi_dogru_adrese_baglanir()
    {
        using var provider = BuildProvider(o => o.Endpoint = LocalEndpoint, name: "ollama");

        using var chatClient = provider.GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(new ModelBinding { Provider = "ollama", Model = "llama3.1" });

        var metadata = chatClient.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>();
        metadata.ProviderUri.ShouldNotBeNull();
        metadata.ProviderUri!.Host.ShouldBe("localhost");
    }

    [Fact]
    public void Iki_adlandirilmis_saglayici_farkli_adreslere_baglanir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .UseOpenAICompatible("openrouter", o => o.Endpoint = OpenRouterEndpoint)
            .UseOpenAICompatible("ollama", o => o.Endpoint = LocalEndpoint);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IModelProviderRegistry>();

        using var openRouterClient = registry.CreateChatClient(
            new ModelBinding { Provider = "openrouter", Model = "openai/gpt-5.4-mini" });
        using var ollamaClient = registry.CreateChatClient(
            new ModelBinding { Provider = "ollama", Model = "llama3.1" });

        var openRouterHost = openRouterClient.GetService(typeof(ChatClientMetadata))
            .ShouldBeOfType<ChatClientMetadata>().ProviderUri!.Host;
        var ollamaHost = ollamaClient.GetService(typeof(ChatClientMetadata))
            .ShouldBeOfType<ChatClientMetadata>().ProviderUri!.Host;

        openRouterHost.ShouldBe("openrouter.example.com");
        ollamaHost.ShouldBe("localhost");
    }

    [Fact]
    public void Responses_yuzeyi_varsayilan_olarak_kaydedilmez()
    {
        using var provider = BuildProvider(o => o.Endpoint = OpenRouterEndpoint);

        var names = provider.GetRequiredService<IModelProviderRegistry>()
            .List().Select(static descriptor => descriptor.Name).ToList();

        names.ShouldNotContain(static name => string.Equals(name, "openrouter-responses", StringComparison.Ordinal));
    }

    [Fact]
    public void Responses_yuzeyi_acikca_istenirse_ikinci_saglayici_kaydedilir()
    {
        using var provider = BuildProvider(o =>
        {
            o.Endpoint = OpenRouterEndpoint;
            o.EnableResponsesSurface = true;
        });

        var names = provider.GetRequiredService<IModelProviderRegistry>()
            .List().Select(static descriptor => descriptor.Name).ToList();

        names.ShouldContain(static name => string.Equals(name, "openrouter", StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, "openrouter-responses", StringComparison.Ordinal));
    }

    [Fact]
    public void Ikinci_cagri_saglayiciyi_cogaltmaz()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .UseOpenAICompatible("openrouter", o => o.Endpoint = OpenRouterEndpoint)
            .UseOpenAICompatible("openrouter", o => o.DefaultModel = "openai/gpt-5.4-mini");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IModelProviderRegistry>().List()
            .Count(static descriptor => string.Equals(descriptor.Name, "openrouter", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public void Ayarlar_yapilandirmadan_okunur()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ApiKey"] = TestData.ApiKey,
                ["Endpoint"] = "https://openrouter.example.com/api/v1",
                ["DefaultModel"] = "openai/gpt-5.4-mini",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAICompatible("openrouter", configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenAIProviderOptions>>().Get("openrouter");

        options.ApiKey.ShouldBe(TestData.ApiKey);
        options.Endpoint.ShouldBe(new Uri("https://openrouter.example.com/api/v1"));
        options.DefaultModel.ShouldBe("openai/gpt-5.4-mini");
    }

    [Fact]
    public void Null_zincir_reddedilir()
        => Should.Throw<ArgumentNullException>(
            () => OpenAICompatibleProviderExtensions.UseOpenAICompatible(null!, "openrouter", static _ => { }));

    [Fact]
    public void Null_yapilandirici_reddedilir()
    {
        var builder = new ServiceCollection().AddAgentPrism();

        Should.Throw<ArgumentNullException>(
            () => builder.UseOpenAICompatible("openrouter", (Action<OpenAIProviderOptions>)null!));
    }

    private static ServiceProvider BuildProvider(Action<OpenAIProviderOptions> configure, string name = "openrouter")
    {
        var services = new ServiceCollection();
        services.AddAgentPrism().UseOpenAICompatible(name, configure);

        return services.BuildServiceProvider();
    }
}
