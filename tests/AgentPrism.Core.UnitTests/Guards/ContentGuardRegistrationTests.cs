using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Icerik denetiminin DI kaydini dogrular.
/// </summary>
/// <remarks>
/// 🚨 En onemli test ilkidir: <c>AddAgentPrism()</c> tek basina hicbir guard
/// kaydetmez. K1'in (sifir surpriz) kapisi bir bayrak degil, <em>kaydin
/// kendisidir</em>; bu test o kapinin kapali dogdugunu olcer.
/// </remarks>
public sealed class ContentGuardRegistrationTests
{
    [Fact]
    public void Varsayilan_kurulumda_hicbir_guard_kayitli_degildir()
    {
        using var provider = Build(services => services.AddAgentPrism());

        provider.GetServices<IContentGuard>().ShouldBeEmpty();
        provider.GetRequiredService<ContentGuardPipeline>().HasGuards.ShouldBeFalse();
    }

    [Fact]
    public void AddPatternContentGuard_yerlesik_guard_i_kaydeder()
    {
        using var provider = Build(services => services.AddAgentPrism().AddPatternContentGuard());

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem().ShouldBeOfType<PatternContentGuard>();
        provider.GetRequiredService<ContentGuardPipeline>().HasGuards.ShouldBeTrue();
    }

    [Fact]
    public void AddPatternContentGuard_ayarlari_kodda_alir()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddPatternContentGuard(options =>
            {
                options.MaskedPii = PiiPatterns.CreditCard;
                options.DeniedTerms.Add("gizli-proje");
            }));

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<PatternContentGuardOptions>>().Value;

        options.MaskedPii.ShouldBe(PiiPatterns.CreditCard);
        options.DeniedTerms.ShouldContain(
            static term => string.Equals(term, "gizli-proje", StringComparison.Ordinal));
    }

    [Fact]
    public void Iki_kez_cagirmak_guard_i_iki_kez_eklemez()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddPatternContentGuard()
            .AddPatternContentGuard());

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Yapilandirma_bolumu_varsa_yerlesik_guard_kaydedilir()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AgentPrism:ContentGuard:Pattern:MaskedPii"] = "Email,CreditCard",
                ["AgentPrism:ContentGuard:Pattern:DeniedTerms:0"] = "gizli-proje",
                ["AgentPrism:ContentGuard:BufferStreamingOutput"] = "false",
            })
            .Build();

        using var provider = Build(services =>
            services.AddAgentPrism(configuration.GetSection(AgentPrismOptions.SectionName)));

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem().ShouldBeOfType<PatternContentGuard>();

        var pattern = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<PatternContentGuardOptions>>().Value;

        pattern.MaskedPii.ShouldBe(PiiPatterns.Email | PiiPatterns.CreditCard);
        pattern.DeniedTerms.ShouldContain(
            static term => string.Equals(term, "gizli-proje", StringComparison.Ordinal));

        provider.GetRequiredService<ContentGuardPipeline>().Options.BufferStreamingOutput.ShouldBeFalse();
    }

    [Fact]
    public void Yapilandirma_bolumu_yoksa_guard_kaydedilmez()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AgentPrism:DefaultTenantId"] = "default",
            })
            .Build();

        using var provider = Build(services =>
            services.AddAgentPrism(configuration.GetSection(AgentPrismOptions.SectionName)));

        provider.GetServices<IContentGuard>().ShouldBeEmpty();
    }

    [Fact]
    public void Tuketicinin_kendi_guard_i_kaydedilebilir()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddContentGuard<AllowAllGuard>());

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem().ShouldBeOfType<AllowAllGuard>();
    }

    [Fact]
    public void Yerlesik_ve_ozel_guard_birlikte_kayitli_olabilir()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddPatternContentGuard()
            .AddContentGuard<AllowAllGuard>());

        provider.GetServices<IContentGuard>().Count().ShouldBe(2);
    }

    [Fact]
    public void Defter_guard_boru_hattini_DI_dan_alir()
    {
        // Kayit zincirinin gercekten baglandigini olcer: bir onceki fazlarda
        // "derleme yesilligi hicbir sey kanitlamaz" dersi bunun icin var.
        using var provider = Build(services =>
        {
            services.AddAgentPrism().AddPatternContentGuard();
            services.AddSingleton<IModelProvider>(new FakeModelProvider(new FakeChatClient()));
        });

        var registry = provider.GetRequiredService<IModelProviderRegistry>();

        using var chatClient = registry.CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(ContentGuardingChatClient)).ShouldNotBeNull();
    }

    [Fact]
    public void Guard_yokken_defter_denetim_sarmalayicisini_eklemez()
    {
        using var provider = Build(services =>
        {
            services.AddAgentPrism();
            services.AddSingleton<IModelProvider>(new FakeModelProvider(new FakeChatClient()));
        });

        using var chatClient = provider
            .GetRequiredService<IModelProviderRegistry>()
            .CreateChatClient(TestData.Binding());

        chatClient.GetService(typeof(ContentGuardingChatClient)).ShouldBeNull();

        // Boru hattinin geri kalani yerinde: tasima hicbir seyi kaybetmedi.
        chatClient.GetService(typeof(Microsoft.Extensions.AI.FunctionInvokingChatClient)).ShouldNotBeNull();
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);

        return services.BuildServiceProvider();
    }

    private sealed class AllowAllGuard : IContentGuard
    {
        public string Name => "allow-all";

        public ValueTask<ContentGuardResult> InspectAsync(
            ContentGuardContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ContentGuardResult.Allow);
    }
}
