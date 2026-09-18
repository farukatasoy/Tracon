using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// MT-CORE-065: <c>Tracon:Pricing:{provider}:{model}</c> reads only the
/// short <c>Input</c>/<c>Output</c> keys (K-021, manual binding). Per K-034, a
/// price entry written with the C# property name (<c>InputCostPerMillionTokens</c>)
/// or another spelling must not drop COMPLETELY SILENTLY.
/// </summary>
public sealed class TraconPricingBindingTests
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
            ["Tracon:Pricing:echo:echo-1:InputCostPerMillionTokens"] = "0.25",
        };

        using var provider = BuildTracon(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TraconOptions>>().Value)
            .Message.ShouldContain("echo:echo-1");
    }

    [Fact]
    public void Voice_price_written_with_the_wrong_key_name_is_rejected_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // The correct key is "PerMillionCharacters"; it was written
            // incorrectly on purpose.
            ["Tracon:Pricing:Voice:elevenlabs:tts-1:PerMillionChars"] = "30.0",
        };

        using var provider = BuildTracon(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TraconOptions>>().Value)
            .Message.ShouldContain("Voice:elevenlabs:tts-1");
    }

    /// <summary>
    /// The rejection reaches the consumer's console, so it must read as English
    /// (K-228). Both messages used to glue a two-letter Turkish correlative
    /// conjunction around the two key names, which the language gate could not
    /// see while it skipped two-letter words. It no longer skips them, so the
    /// phrase cannot be quoted here either — see
    /// <see cref="Architecture.SourceLanguageTests"/>.
    /// </summary>
    [Fact]
    public void Rejection_of_a_provider_price_key_reads_as_English()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:echo:echo-1:InputCostPerMillionTokens"] = "0.25",
        };

        using var provider = BuildTracon(configValues);

        var message = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconOptions>>().Value).Message;

        message.ShouldContain("'echo:echo-1' contains neither 'Input' nor 'Output'. Check the key name.");
    }

    /// <summary>Same boundary as the provider message above; see its remarks.</summary>
    [Fact]
    public void Rejection_of_a_voice_price_key_reads_as_English()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:Voice:elevenlabs:tts-1:PerMillionChars"] = "30.0",
        };

        using var provider = BuildTracon(configValues);

        var message = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<TraconOptions>>().Value).Message;

        message.ShouldContain(
            "'Voice:elevenlabs:tts-1' contains neither 'PerMillionCharacters' nor 'PerMinute'. " +
            "Check the key name.");
    }

    [Fact]
    public void Price_written_with_the_correct_key_is_accepted_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:echo:echo-1:Input"] = "0.25",
            ["Tracon:Pricing:echo:echo-1:Output"] = "1.0",
        };

        using var provider = BuildTracon(configValues);

        var options = provider.GetRequiredService<IOptions<TraconOptions>>().Value;

        options.Pricing.Providers["echo"]["echo-1"].InputCostPerMillionTokens.ShouldBe(0.25m);
    }

    /// <summary>
    /// Calls <c>TraconServiceCollectionExtensions.Bind</c> (the private,
    /// manually written K-021 binder) directly, without DI or the validator
    /// kicking in. Purpose: these tests measure only BINDING behavior — the
    /// validation tests further down verify the same scenario as a startup
    /// failure.
    /// </summary>
    private static TraconOptions BindOnly(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var options = new TraconOptions();

        var bind = typeof(TraconOptions).Assembly
            .GetType("Tracon.TraconServiceCollectionExtensions")!
            .GetMethod(
                "Bind",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(IConfiguration), typeof(TraconOptions)])
            ?? throw new InvalidOperationException("TraconServiceCollectionExtensions.Bind not found.");

        bind.Invoke(null, [configuration, options]);

        return options;
    }

    private static ServiceProvider BuildTracon(Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        return services.BuildServiceProvider();
    }
}
