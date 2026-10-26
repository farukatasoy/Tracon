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

    // --- F-603: Tracon:Pricing:HostedTools:{provider}:{kind}:PerCall ---

    [Fact]
    public void A_hosted_tool_price_is_bound_by_provider_and_kind_and_the_key_is_not_a_provider()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Pricing:HostedTools:openai-responses:WebSearch:PerCall"] = "0.01",
            ["Pricing:HostedTools:openai-responses:CodeInterpreter:PerCall"] = "0.03",
            ["Pricing:HostedTools:anthropic:WebSearch:PerCall"] = "0.01",
        };

        var options = BindOnly(configValues);

        options.Pricing.HostedTools["openai-responses"]["WebSearch"].PerCall.ShouldBe(0.01m);
        options.Pricing.HostedTools["openai-responses"]["CodeInterpreter"].PerCall.ShouldBe(0.03m);
        options.Pricing.HostedTools["anthropic"]["websearch"].PerCall.ShouldBe(0.01m, "kind names compare without case");
        options.Pricing.Providers.ShouldNotContainKey("HostedTools");
    }

    [Fact]
    public void A_hosted_tool_price_for_a_kind_that_does_not_exist_is_rejected_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:HostedTools:openai-responses:Websearch2:PerCall"] = "0.01",
        };

        using var provider = BuildTracon(configValues);

        var message = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TraconOptions>>().Value).Message;

        message.ShouldContain("'HostedTools:openai-responses:Websearch2' is not a hosted tool kind");
        message.ShouldContain("WebSearch, CodeInterpreter, FileSearch");
    }

    [Fact]
    public void A_hosted_tool_price_written_with_the_wrong_key_name_is_rejected_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:HostedTools:openai-responses:WebSearch:Price"] = "0.01",
        };

        using var provider = BuildTracon(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TraconOptions>>().Value)
            .Message.ShouldContain("'HostedTools:openai-responses:WebSearch' does not contain 'PerCall'. Check the key name.");
    }

    [Fact]
    public void A_negative_hosted_tool_price_is_rejected_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:HostedTools:openai-responses:WebSearch:PerCall"] = "-0.01",
        };

        using var provider = BuildTracon(configValues);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TraconOptions>>().Value)
            .Message.ShouldContain("price for 'HostedTools:openai-responses:WebSearch' cannot be negative");
    }

    [Fact]
    public void A_hosted_tool_price_written_correctly_is_accepted_at_startup()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:HostedTools:openai-responses:FileSearch:PerCall"] = "0.0025",
        };

        using var provider = BuildTracon(configValues);

        provider.GetRequiredService<IOptions<TraconOptions>>().Value
            .Pricing.HostedTools["openai-responses"]["FileSearch"].PerCall.ShouldBe(0.0025m);
    }

    [Theory]
    [InlineData("Tracon:Pricing:echo:echo-1:Input", "1,5")]
    [InlineData("Tracon:Pricing:echo:echo-1:Output", "two")]
    [InlineData("Tracon:Pricing:echo:echo-1:CachedInput", "0,1")]
    [InlineData("Tracon:Pricing:Voice:eleven:flash:PerMinute", "cheap")]
    [InlineData("Tracon:Pricing:Voice:eleven:flash:PerMillionCharacters", "1,5")]
    [InlineData("Tracon:Pricing:HostedTools:openai-responses:WebSearch:PerCall", "0,01")]
    [InlineData("Tracon:Pricing:Images:openai:gpt-image:PerImage", "free")]
    [InlineData("Tracon:Pricing:Images:openai:gpt-image:OutputCostPerMillionTokens", "1,5")]
    [InlineData("Tracon:Pricing:Images:openai:gpt-image:SizeMultipliers:1024x1024", "double")]
    public void A_price_that_cannot_be_read_stops_the_host_and_is_named_by_its_key(string key, string text)
    {
        // The reader left an unreadable price null, which made the model unpriced without a word. A decimal
        // takes no thousands separator: "1,5" used to be read as fifteen.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = text });

        failure.Message.ShouldContain($"{key} is '{text}'");
    }

    [Fact]
    public void A_price_with_a_thousands_separator_is_not_read_as_a_bigger_number()
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:echo:echo-1:Input"] = "1,000.5",
            ["Tracon:Pricing:echo:echo-1:Output"] = "2",
        });

        failure.Message.ShouldContain("Tracon:Pricing:echo:echo-1:Input is '1,000.5'");
    }

    [Fact]
    public void Code_that_replaces_the_price_entry_wins_over_an_unreadable_price()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:echo:echo-1:Input"] = "1,5",
            ["Tracon:Pricing:echo:echo-1:Output"] = "2",
        };

        SectionBinding.StartsCleanly(values, static (builder, _) =>
            builder.Services.Configure<TraconOptions>(static options =>
                options.Pricing.Providers["echo"]["echo-1"] = new ModelPriceOverride
                {
                    InputCostPerMillionTokens = 1.5m,
                    OutputCostPerMillionTokens = 2m,
                }));
    }

    [Fact]
    public void An_empty_price_means_not_given()
    {
        var options = BindOnly(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Pricing:echo:echo-1:Input"] = "0.25",
            ["Pricing:echo:echo-1:Output"] = string.Empty,
            ["Pricing:echo:echo-1:CachedInput"] = "  ",
        });

        options.Pricing.Providers["echo"]["echo-1"].InputCostPerMillionTokens.ShouldBe(0.25m);
        options.Pricing.Providers["echo"]["echo-1"].OutputCostPerMillionTokens.ShouldBeNull();
        options.Pricing.Providers["echo"]["echo-1"].CachedInputCostPerMillionTokens.ShouldBeNull();
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
