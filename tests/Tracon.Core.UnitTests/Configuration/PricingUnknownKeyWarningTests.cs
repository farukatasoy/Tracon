using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// A price key the binder does not read is named at startup; the host still starts.
/// </summary>
/// <remarks>
/// 🚨 What this closes: the binder reads five short keys per model and never compared the children of a model section with
/// that set. A host that wrote <c>CacheRead</c> (or the code property name <c>CachedInputCostPerMillionTokens</c>) beside
/// <c>Input</c> and <c>Output</c> started cleanly and priced its cached tokens at the plain input rate for as long as nobody
/// compared the report with the provider's bill. The check is advisory: it never rejects, because a configuration that has
/// worked until now must keep starting.
/// </remarks>
public sealed class PricingUnknownKeyWarningTests
{
    private const string Prefix = "Tracon:Pricing:openai:gpt-x:";

    [Fact]
    public async Task A_cache_read_key_beside_input_and_output_is_named_with_the_key_the_binder_reads()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Prefix + "Input"] = "1",
            [Prefix + "Output"] = "2",
            [Prefix + "CacheRead"] = "0.1",
        });

        var warning = warnings.ShouldHaveSingleItem();

        warning.ShouldContain(Prefix + "CacheRead");
        warning.ShouldContain("CachedInput", customMessage: "the warning must say which key the binder reads instead");
        warning.ShouldContain("Input, Output, CachedInput, CacheWrite, CacheWriteLong", customMessage: "the warning must list the keys that are read");
    }

    [Fact]
    public async Task The_code_property_name_of_the_cached_rate_is_named_with_its_short_key()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Prefix + "Input"] = "1",
            [Prefix + "Output"] = "2",
            [Prefix + "CachedInputCostPerMillionTokens"] = "0.1",
        });

        var warning = warnings.ShouldHaveSingleItem();

        warning.ShouldContain(Prefix + "CachedInputCostPerMillionTokens");
        warning.ShouldContain("CachedInput");
    }

    [Fact]
    public async Task Known_keys_are_not_named_whatever_their_case()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Prefix + "input"] = "1",
            [Prefix + "OUTPUT"] = "2",
            [Prefix + "cachedinput"] = "0.1",
            [Prefix + "CACHEWRITE"] = "1.25",
            [Prefix + "cachewritelong"] = "2",
        });

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_with_no_likely_match_is_still_named_and_suggests_nothing()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Prefix + "Input"] = "1",
            [Prefix + "Frobnicate"] = "7",
        });

        var warning = warnings.ShouldHaveSingleItem();

        warning.ShouldContain(Prefix + "Frobnicate");
        warning.ShouldNotContain("did you mean");
    }

    [Fact]
    public async Task Every_unknown_key_of_every_model_is_named_in_one_warning()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:openai:gpt-x:Input"] = "1",
            ["Tracon:Pricing:openai:gpt-x:CacheRead"] = "0.1",
            ["Tracon:Pricing:anthropic:claude-x:Output"] = "3",
            ["Tracon:Pricing:anthropic:claude-x:CacheReads"] = "0.3",
            ["Tracon:Pricing:anthropic:claude-x:Frobnicate"] = "9",
        });

        var warning = warnings.ShouldHaveSingleItem();

        warning.ShouldContain("3 price key(s)");
        warning.ShouldContain("Tracon:Pricing:openai:gpt-x:CacheRead");
        warning.ShouldContain("Tracon:Pricing:anthropic:claude-x:CacheReads");
        warning.ShouldContain("Tracon:Pricing:anthropic:claude-x:Frobnicate");
    }

    [Fact]
    public async Task The_reserved_sections_are_not_read_as_providers_and_say_nothing()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Pricing:Currency"] = "USD",
            ["Tracon:Pricing:Voice:elevenlabs:scribe_v2:PerMinute"] = "0.4",
            ["Tracon:Pricing:Images:openai:gpt-image-1:PerImage"] = "0.04",
            ["Tracon:Pricing:HostedTools:openai-responses:WebSearch:PerCall"] = "0.01",
            ["Tracon:Pricing:openai:gpt-x:Input"] = "1",
        });

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_host_without_a_pricing_section_says_nothing()
    {
        var warnings = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Observability:SuccessSampleRatio"] = "1",
        });

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_key_does_not_stop_the_host()
    {
        // The options validator rejects a model with neither Input nor Output, and a model that has one of them stays valid:
        // the new check must not add a rejection of its own.
        SectionBinding.StartsCleanly(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Prefix + "Input"] = "1",
            [Prefix + "Output"] = "2",
            [Prefix + "CacheRead"] = "0.1",
        });
    }

    [Fact]
    public void The_check_is_an_advisory()
    {
        using var provider = SectionBinding.Build(SectionBinding.NoKeys, compose: null);

        var checks = provider.GetServices<ITraconStartupCheck>()
            .Where(static check => check.GetType().Name.Contains("PricingKey", StringComparison.Ordinal))
            .ToList();

        checks.ShouldHaveSingleItem().Kind.ShouldBe(StartupCheckKind.Advisory);
    }

    /// <summary>Starts the start-up checks the way the host does and returns the warnings that name the pricing section.</summary>
    private static async Task<IReadOnlyList<string>> StartAsync(IReadOnlyDictionary<string, string?> values)
    {
        var logs = new WarningLogProvider();

        using var provider = SectionBinding.Build(
            values,
            (builder, _) => builder.Services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(logs);
            }));

        var checks = provider.GetServices<IHostedService>().OfType<TraconStartupChecks>().Single();

        await checks.StartAsync(TestContext.Current.CancellationToken);

        return [.. logs.Warnings.Where(static warning => warning.Contains("Tracon:Pricing", StringComparison.Ordinal))];
    }
}
