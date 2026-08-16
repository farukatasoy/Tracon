using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Configuration;

/// <summary>
/// MT-CORE-065: <c>AgentPrism:Pricing:{provider}:{model}</c> reads only the
/// short <c>Input</c>/<c>Output</c> keys (K-021, manual binding). Per K-034, a
/// price entry written with the C# property name (<c>InputCostPerMillionTokens</c>)
/// or another spelling must not drop COMPLETELY SILENTLY.
/// </summary>
public sealed class AgentPrismPricingBindingTests
{
    [Fact]
    public void Price_written_with_the_wrong_key_name_is_not_dropped_from_Providers()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // The correct keys are "Input"/"Output"; the C# property name was
            // written intentionally.
            ["Pricing:echo:echo-1:InputCostPerMillionTokens"] = "0.25",
            ["Pricing:echo:echo-1:OutputCostPerMillionTokens"] = "1.0",
        };

        var options = BindOnly(configValues);

        // The model does not disappear entirely; both enter Providers as an
        // empty record — the validator rejects this at startup (see the test
        // below).
        options.Pricing.Providers.ShouldContainKey("echo");
        options.Pricing.Providers["echo"].ShouldContainKey("echo-1");
        options.Pricing.Providers["echo"]["echo-1"].InputCostPerMillionTokens.ShouldBeNull();
        options.Pricing.Providers["echo"]["echo-1"].OutputCostPerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public void Price_written_with_the_correct_short_key_is_accepted()
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
    public void Price_written_with_the_wrong_key_name_is_rejected_at_startup()
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
    public void Voice_price_written_with_the_wrong_key_name_is_rejected_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // The correct key is "PerMillionCharacters"; it was written
            // incorrectly on purpose.
            ["AgentPrism:Pricing:Voice:elevenlabs:tts-1:PerMillionChars"] = "30.0",
        };

        using var provider = BuildAgentPrism(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value)
            .Message.ShouldContain("Voice:elevenlabs:tts-1");
    }

    [Fact]
    public void Price_written_with_the_correct_key_is_accepted_at_startup()
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
    /// Calls <c>AgentPrismServiceCollectionExtensions.Bind</c> (the private,
    /// manually written K-021 binder) directly, without DI or the validator
    /// kicking in. Purpose: these tests measure only BINDING behavior — the
    /// validation tests further down verify the same scenario as a startup
    /// failure.
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
            ?? throw new InvalidOperationException("AgentPrismServiceCollectionExtensions.Bind not found.");

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
