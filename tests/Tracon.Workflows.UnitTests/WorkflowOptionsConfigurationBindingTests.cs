using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <c>UseWorkflows()</c> actually binds every <c>Tracon:Workflows</c> key.
/// </summary>
/// <remarks>
/// The binder is written by hand, so an option that is added to <see cref="TraconWorkflowOptions"/> and not to the
/// binder would compile, pass every other test and ignore the operator's value without a word. The first test below
/// writes a value that differs from the default for EVERY public settable option, found by reflection, and fails for
/// the one the binder does not read.
/// </remarks>
public sealed class WorkflowOptionsConfigurationBindingTests
{
    /// <summary>The text written for each key, and the value it has to bind to. Every value differs from the default.</summary>
    private static readonly Dictionary<string, (string Text, object Expected)> Samples = new(StringComparer.Ordinal)
    {
        [nameof(TraconWorkflowOptions.Enabled)] = ("false", false),
        [nameof(TraconWorkflowOptions.EnableCheckpointing)] = ("false", false),
        [nameof(TraconWorkflowOptions.MaxConcurrentRuns)] = ("7", 7),
        [nameof(TraconWorkflowOptions.MaxConcurrentRunsPerTenant)] = ("3", 3),
        [nameof(TraconWorkflowOptions.RunTimeout)] = ("00:05:00", TimeSpan.FromMinutes(5)),
        [nameof(TraconWorkflowOptions.MaxRunTimeout)] = ("1.00:00:00", TimeSpan.FromDays(1)),
        [nameof(TraconWorkflowOptions.MaxSuperSteps)] = ("11", 11),
        [nameof(TraconWorkflowOptions.MaxNodeSettingsLength)] = ("2048", 2048),
        [nameof(TraconWorkflowOptions.KeepCheckpointsAfterCompletion)] = ("false", false),
        [nameof(TraconWorkflowOptions.ResumeInterruptedRuns)] = ("true", true),
        [nameof(TraconWorkflowOptions.MaxResumeAttempts)] = ("4", 4),
        [nameof(TraconWorkflowOptions.RequestExpirationScanInterval)] = ("00:02:30", TimeSpan.FromSeconds(150)),
        [nameof(TraconWorkflowOptions.MaxRequestExpirationsPerScan)] = ("17", 17),
    };

    [Fact]
    public void Every_option_has_a_sample_and_every_sample_names_an_option()
    {
        var properties = Settable().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);

        properties.Except(Samples.Keys, StringComparer.Ordinal).ShouldBeEmpty("An option has no sample: write one, and bind the key in UseWorkflows.");
        Samples.Keys.Except(properties, StringComparer.Ordinal).ShouldBeEmpty("A sample names an option that does not exist.");
    }

    [Fact]
    public void Every_sample_differs_from_the_default()
    {
        var defaults = new TraconWorkflowOptions();

        foreach (var property in Settable())
        {
            Samples[property.Name].Expected.ShouldNotBe(
                property.GetValue(defaults),
                $"{property.Name}: a sample equal to the default cannot show that the key is bound.");
        }
    }

    [Fact]
    public void The_configuration_section_binds_every_option()
    {
        // The options as the container builds them, before validation: the combination of every sample is not a
        // valid setup (resuming runs needs checkpointing, which one sample turns off), and this test is about the
        // reading, not the rule.
        var options = Build(Samples.ToDictionary(
            static pair => $"{TraconWorkflowOptions.SectionName}:{pair.Key}",
            static pair => (string?)pair.Value.Text,
            StringComparer.Ordinal));

        var failures = new List<string>();

        foreach (var property in Settable())
        {
            var bound = property.GetValue(options);

            if (!Equals(bound, Samples[property.Name].Expected))
            {
                failures.Add($"{property.Name}: the option holds '{bound}', not '{Samples[property.Name].Expected}'.");
            }
        }

        failures.ShouldBeEmpty(customMessage: $"A key in {TraconWorkflowOptions.SectionName} is not read:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void An_empty_section_keeps_the_defaults()
    {
        var defaults = new TraconWorkflowOptions();
        var options = Build([]);

        foreach (var property in Settable())
        {
            property.GetValue(options).ShouldBe(property.GetValue(defaults), $"{property.Name} must keep its default.");
        }
    }

    /// <summary>A value given in code OVERWRITES the value coming from configuration: the section is read first, the delegate after.</summary>
    [Fact]
    public void Code_side_configure_overwrites_configuration()
    {
        var options = Resolve(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Workflows:MaxSuperSteps"] = "7",
                ["Tracon:Workflows:RunTimeout"] = "00:05:00",
            },
            configure: o => o.MaxSuperSteps = 42);

        options.MaxSuperSteps.ShouldBe(42);
        options.RunTimeout.ShouldBe(TimeSpan.FromMinutes(5), "a key the code did not set keeps the configured value");
    }

    [Fact]
    public void The_options_pass_through_the_container_with_their_configured_values()
    {
        var options = Resolve(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Workflows:MaxConcurrentRuns"] = "9",
            ["Tracon:Workflows:MaxConcurrentRunsPerTenant"] = "2",
            ["Tracon:Workflows:MaxRunTimeout"] = "02:00:00",
        });

        options.MaxConcurrentRuns.ShouldBe(9);
        options.MaxConcurrentRunsPerTenant.ShouldBe(2);
        options.MaxRunTimeout.ShouldBe(TimeSpan.FromHours(2));
    }

    private static List<PropertyInfo> Settable()
        => typeof(TraconWorkflowOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0 && property.SetMethod is { IsPublic: true })
            .ToList();

    /// <summary>
    /// Builds the options through the REAL registration path (<c>AddTracon().UseWorkflows()</c>), applying the
    /// registered configuration steps without the validators.
    /// </summary>
    private static TraconWorkflowOptions Build(Dictionary<string, string?> settings)
    {
        using var provider = Compose(settings, configure: null);

        var options = new TraconWorkflowOptions();

        foreach (var step in provider.GetServices<IConfigureOptions<TraconWorkflowOptions>>())
        {
            step.Configure(options);
        }

        return options;
    }

    /// <summary>Resolves the options the way the runner does: <c>IOptions&lt;T&gt;</c>, validators included.</summary>
    private static TraconWorkflowOptions Resolve(
        Dictionary<string, string?> settings,
        Action<TraconWorkflowOptions>? configure = null)
    {
        using var provider = Compose(settings, configure);

        return provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value;
    }

    private static ServiceProvider Compose(
        Dictionary<string, string?> settings,
        Action<TraconWorkflowOptions>? configure)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows(configure);

        return services.BuildServiceProvider();
    }
}
