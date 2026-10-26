using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.ProviderCore.Tests;

/// <summary>
/// The shared registration and binding steps (phase 181). Linked into the four
/// provider test projects.
/// </summary>
public sealed class ProviderRegistrationCoreTests
{
    [Fact]
    public void Every_ModelDescriptor_property_is_bound_from_configuration()
    {
        // K-483 class: a field added to ModelDescriptor but not to BindModels is
        // silently never read. Before phase 181 the binding was copied four times.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["0:Name"] = "model-a",
            ["0:DisplayName"] = "Model A",
            ["0:ContextWindowTokens"] = "1000",
            ["0:MaxOutputTokens"] = "200",
            ["0:SupportsStreaming"] = "false",
            ["0:SupportsTools"] = "false",
            ["0:SupportsReasoning"] = "true",
            ["0:SupportsStructuredOutput"] = "true",
            ["0:SupportsSampling"] = "false",
            ["0:InputCostPerMillionTokens"] = "1.5",
            ["0:OutputCostPerMillionTokens"] = "2.5",
            ["0:CachedInputCostPerMillionTokens"] = "0.5",
        };

        var settable = typeof(ModelDescriptor)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanWrite)
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        values.Keys.Select(static key => key[2..]).Order(StringComparer.Ordinal).ToList().ShouldBe(
            settable,
            customMessage: "A ModelDescriptor property has no configuration key in this test. Add it here AND to ProviderRegistrationCore.BindModels.");

        var models = new List<ModelDescriptor>();
        ProviderRegistrationCore.BindModels(Configuration(values), models, new SampleOptions());

        var model = models.ShouldHaveSingleItem();
        var defaults = new ModelDescriptor { Name = string.Empty };

        foreach (var name in settable)
        {
            var property = typeof(ModelDescriptor).GetProperty(name)!;
            property.GetValue(model).ShouldNotBe(
                property.GetValue(defaults),
                $"ModelDescriptor.{name} was not read by BindModels.");
        }
    }

    [Fact]
    public void Nameless_model_is_kept_for_the_validator_to_reject()
    {
        var models = new List<ModelDescriptor>();

        ProviderRegistrationCore.BindModels(
            Configuration(new Dictionary<string, string?>(StringComparer.Ordinal) { ["0:DisplayName"] = "no name" }),
            models,
            new SampleOptions());

        models.ShouldHaveSingleItem().Name.ShouldBeEmpty();
    }

    [Fact]
    public void Relative_endpoint_is_kept_for_the_validator_to_reject()
    {
        var endpoint = ProviderRegistrationCore.ReadEndpoint(
            Configuration(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Endpoint"] = "just-a-path" }),
            "Endpoint");

        endpoint.ShouldNotBeNull();
        endpoint.IsAbsoluteUri.ShouldBeFalse();
    }

    [Fact]
    public void Catalog_numbers_use_the_invariant_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

        try
        {
            var models = new List<ModelDescriptor>();

            ProviderRegistrationCore.BindModels(
                Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["0:Name"] = "model-a",
                    ["0:InputCostPerMillionTokens"] = "1.5",
                }),
                models,
                new SampleOptions());

            models.ShouldHaveSingleItem().InputCostPerMillionTokens.ShouldBe(1.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void An_unreadable_catalog_field_is_left_unset_and_recorded()
    {
        var options = new SampleOptions();
        var models = new List<ModelDescriptor>();

        ProviderRegistrationCore.BindModels(
            Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["0:Name"] = "model-a",
                ["0:ContextWindowTokens"] = "128k",
                ["0:SupportsTools"] = "evet",
                ["0:InputCostPerMillionTokens"] = "1,5",
                ["0:OutputCostPerMillionTokens"] = "2.5",
            }),
            models,
            options);

        var model = models.ShouldHaveSingleItem();

        model.ContextWindowTokens.ShouldBeNull();
        model.InputCostPerMillionTokens.ShouldBeNull();
        model.SupportsTools.ShouldBeTrue();
        model.OutputCostPerMillionTokens.ShouldBe(2.5m);

        UnreadableConfigurationReport.TryGet(options, out var report).ShouldBeTrue();

        var failures = report!.Failures();

        failures.Count.ShouldBe(3);
        failures.ShouldContain(static failure => failure.StartsWith("0:ContextWindowTokens is '128k'", StringComparison.Ordinal));
        failures.ShouldContain(static failure => failure.StartsWith("0:SupportsTools is 'evet'", StringComparison.Ordinal));
        failures.ShouldContain(static failure => failure.StartsWith("0:InputCostPerMillionTokens is '1,5'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreadable_catalog_field_stops_counting_once_code_removes_the_model()
    {
        var options = new SampleOptions();
        var models = new List<ModelDescriptor>();

        ProviderRegistrationCore.BindModels(
            Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["0:Name"] = "model-a",
                ["0:ContextWindowTokens"] = "128k",
            }),
            models,
            options);

        UnreadableConfigurationReport.For(options).Failures().ShouldHaveSingleItem();

        models.Clear();

        UnreadableConfigurationReport.For(options).Failures().ShouldBeEmpty();
    }

    [Fact]
    public void An_absent_endpoint_reads_as_null()
    {
        var section = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Number"] = "abc" });

        ProviderRegistrationCore.ReadEndpoint(section, "Missing").ShouldBeNull();
    }

    [Fact]
    public void Options_registered_twice_keep_one_validator_and_merge_both_steps()
    {
        var services = new ServiceCollection();

        ProviderRegistrationCore.AddValidatedOptions<SampleOptions, SampleValidator>(services, static options => options.First = "1");
        ProviderRegistrationCore.AddValidatedOptions<SampleOptions, SampleValidator>(services, static options => options.Second = "2");

        services.Count(static descriptor => descriptor.ServiceType == typeof(IValidateOptions<SampleOptions>)
            && descriptor.ImplementationType == typeof(SampleValidator)).ShouldBe(1);
        services.Count(static descriptor => descriptor.ServiceType == typeof(IValidateOptions<SampleOptions>)
            && descriptor.ImplementationType == typeof(UnreadableConfigurationValidator<SampleOptions>)).ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SampleOptions>>().Value;

        options.First.ShouldBe("1");
        options.Second.ShouldBe("2");
    }

    [Fact]
    public void Registration_marker_is_detected()
    {
        var services = new ServiceCollection();

        ProviderRegistrationCore.IsRegistered<SampleOptions>(services).ShouldBeFalse();

        services.AddSingleton(new SampleOptions());

        ProviderRegistrationCore.IsRegistered<SampleOptions>(services).ShouldBeTrue();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    public sealed class SampleOptions
    {
        public string? First { get; set; }

        public string? Second { get; set; }
    }

    public sealed class SampleValidator : IValidateOptions<SampleOptions>
    {
        public ValidateOptionsResult Validate(string? name, SampleOptions options) => ValidateOptionsResult.Success;
    }
}
