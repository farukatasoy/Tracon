using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Verifies the DI registration of content inspection.
/// </summary>
/// <remarks>
/// 🚨 The most important test comes first: <c>AddAgentPrism()</c> alone
/// registers no guard. K1's (zero surprise) gate is not a flag, it is
/// <em>the registration itself</em>; this test measures that the gate is
/// born closed.
/// </remarks>
public sealed class ContentGuardRegistrationTests
{
    [Fact]
    public void No_guard_is_registered_in_the_default_setup()
    {
        using var provider = Build(services => services.AddAgentPrism());

        provider.GetServices<IContentGuard>().ShouldBeEmpty();
        provider.GetRequiredService<ContentGuardPipeline>().HasGuards.ShouldBeFalse();
    }

    [Fact]
    public void AddPatternContentGuard_registers_the_built_in_guard()
    {
        using var provider = Build(services => services.AddAgentPrism().AddPatternContentGuard());

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem().ShouldBeOfType<PatternContentGuard>();
        provider.GetRequiredService<ContentGuardPipeline>().HasGuards.ShouldBeTrue();
    }

    [Fact]
    public void AddPatternContentGuard_takes_settings_from_code()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddPatternContentGuard(options =>
            {
                options.MaskedPii = PiiPatterns.CreditCard;
                options.DeniedTerms.Add("secret-project");
            }));

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<PatternContentGuardOptions>>().Value;

        options.MaskedPii.ShouldBe(PiiPatterns.CreditCard);
        options.DeniedTerms.ShouldContain(
            static term => string.Equals(term, "secret-project", StringComparison.Ordinal));
    }

    [Fact]
    public void Calling_it_twice_does_not_add_the_guard_twice()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddPatternContentGuard()
            .AddPatternContentGuard());

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem();
    }

    [Fact]
    public void When_the_configuration_section_is_present_the_built_in_guard_is_registered()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["AgentPrism:ContentGuard:Pattern:MaskedPii"] = "Email,CreditCard",
                ["AgentPrism:ContentGuard:Pattern:DeniedTerms:0"] = "secret-project",
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
            static term => string.Equals(term, "secret-project", StringComparison.Ordinal));

        provider.GetRequiredService<ContentGuardPipeline>().Options.BufferStreamingOutput.ShouldBeFalse();
    }

    [Fact]
    public void When_the_configuration_section_is_absent_no_guard_is_registered()
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
    public void A_consumers_own_guard_can_be_registered()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddContentGuard<AllowAllGuard>());

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem().ShouldBeOfType<AllowAllGuard>();
    }

    [Fact]
    public void A_configured_guard_instance_can_be_registered()
    {
        var guard = new AllowAllGuard();

        using var provider = Build(services => services
            .AddAgentPrism()
            .AddContentGuard(guard));

        provider.GetServices<IContentGuard>().ShouldHaveSingleItem().ShouldBeSameAs(guard);
    }

    [Fact]
    public void Multiple_configured_guard_instances_are_all_preserved()
    {
        var first = new AllowAllGuard();
        var second = new AllowAllGuard();

        using var provider = Build(services => services
            .AddAgentPrism()
            .AddContentGuard(first)
            .AddContentGuard(second));

        provider.GetServices<IContentGuard>().OfType<AllowAllGuard>().ShouldBe([first, second]);
    }

    [Fact]
    public void A_guard_factory_runs_once_and_preserves_multiple_configurations()
    {
        var calls = 0;

        using var provider = Build(services => services
            .AddAgentPrism()
            .AddContentGuard(_ =>
            {
                calls++;
                return new AllowAllGuard();
            })
            .AddContentGuard(_ => new AllowAllGuard()));

        provider.GetServices<IContentGuard>().OfType<AllowAllGuard>().Count().ShouldBe(2);
        calls.ShouldBe(1);
    }

    [Fact]
    public void The_built_in_guard_and_a_custom_guard_can_be_registered_together()
    {
        using var provider = Build(services => services
            .AddAgentPrism()
            .AddPatternContentGuard()
            .AddContentGuard<AllowAllGuard>());

        provider.GetServices<IContentGuard>().Count().ShouldBe(2);
    }

    [Fact]
    public void The_registry_picks_up_the_guard_pipeline_from_DI()
    {
        // Measures that the registration chain is actually wired up: this
        // exists because of a lesson from earlier phases — "a green build
        // proves nothing".
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
    public void Without_a_guard_the_registry_does_not_add_the_inspection_decorator()
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

        // The rest of the pipeline is intact: the decorator did not lose anything.
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
