using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A <c>Tracon:Workflows</c> value whose text cannot be read stops the host at startup, and the message names the
/// key and the text.
/// </summary>
/// <remarks>
/// <para>
/// The section used to be read by the framework's configuration binder. For an option that is not nullable it threw a
/// raw <see cref="InvalidOperationException"/> out of the first read of the options, and a time span written as
/// <c>30</c> or <c>24:00:00</c> was read as 30 or 24 days. The binder is now written by hand and reads every scalar
/// through <c>ConfigurationValueReader</c>, like every other section, so the host refuses to start with one list of
/// the keys it could not read.
/// </para>
/// <para>
/// The probe walks the public settable scalars of the options type by reflection, so a scalar added later is covered
/// without anyone remembering to add a case. Every case composes the container the way a host does
/// (<c>AddTracon().UseWorkflows()</c> over the container's <see cref="IConfiguration"/>) and starts it through
/// <see cref="IStartupValidator"/>: reading <c>IOptions&lt;T&gt;.Value</c> runs a validator too, so it would not prove
/// that the application refuses to start.
/// </para>
/// </remarks>
public sealed class WorkflowsUnreadableConfigurationTests
{
    private const string Unreadable = "not-readable";

    [Fact]
    public void Every_scalar_stops_the_host_when_its_text_cannot_be_read()
    {
        var scalars = Scalars();

        // The options hold thirteen scalars; a scan that found few would pass for the wrong reason.
        scalars.Count.ShouldBeGreaterThanOrEqualTo(13);

        var failures = new List<string>();

        foreach (var property in scalars)
        {
            var key = KeyOf(property);
            using var provider = Compose(Settings((key, Unreadable)));

            var failure = StartupFailure(provider);

            if (failure is null)
            {
                failures.Add($"{key}: the host did not stop for text it cannot read.");
            }
            else if (!failure.Message.Contains($"{key} is '{Unreadable}'", StringComparison.Ordinal))
            {
                failures.Add($"{key}: the host stopped, but the message does not name the key and the text: {failure.Message}");
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A scalar is read by the framework binder, or with the skip-and-keep-the-default pattern, or not read at all:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_settable_property_is_a_probed_scalar()
    {
        var unaccounted = SettableProperties()
            .Where(static property => !IsReadScalar(property.PropertyType))
            .Select(static property => $"{property.Name} ({property.PropertyType.Name})")
            .ToList();

        unaccounted.ShouldBeEmpty(
            customMessage: "The probe writes scalars only. A property of another type is read by the binder in some other way: " +
                           $"teach this probe how to write it. {string.Join(", ", unaccounted)}");
    }

    [Theory]
    [InlineData("Tracon:Workflows:MaxRunTimeout", "30", "a time span")]
    [InlineData("Tracon:Workflows:MaxRunTimeout", "24:00:00", "a time span")]
    [InlineData("Tracon:Workflows:RunTimeout", "10m", "a time span")]
    [InlineData("Tracon:Workflows:MaxConcurrentRunsPerTenant", "abc", "a 32-bit whole number")]
    [InlineData("Tracon:Workflows:ResumeInterruptedRuns", "evet", "true or false")]
    public void An_unreadable_value_stops_the_host_and_is_named(string key, string text, string expected)
    {
        using var provider = Compose(Settings((key, text)));

        var failure = StartupFailure(provider);

        failure.ShouldNotBeNull();
        failure.Message.ShouldContain($"{key} is '{text}'");
        failure.Message.ShouldContain(expected);
        failure.Message.ShouldContain("remove the key to keep the default");
    }

    [Fact]
    public void Several_unreadable_values_are_listed_together()
    {
        using var provider = Compose(Settings(
            ("Tracon:Workflows:MaxRunTimeout", "30"),
            ("Tracon:Workflows:MaxConcurrentRunsPerTenant", "abc"),
            ("Tracon:Workflows:EnableCheckpointing", "evet")));

        var failure = StartupFailure(provider);

        failure.ShouldNotBeNull();
        failure.Failures.Count().ShouldBe(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_blank_value_means_not_given_and_keeps_the_default(string text)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var property in Scalars())
        {
            values[KeyOf(property)] = text;
        }

        using var provider = Compose(values);

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value;
        var defaults = new TraconWorkflowOptions();

        foreach (var property in Scalars())
        {
            property.GetValue(options).ShouldBe(property.GetValue(defaults), $"{property.Name} must keep its default.");
        }
    }

    [Fact]
    public void Readable_text_binds_and_the_host_starts()
    {
        using var provider = Compose(Settings(
            ("Tracon:Workflows:RunTimeout", "00:05:00"),
            ("Tracon:Workflows:MaxRunTimeout", "1.00:00:00"),
            ("Tracon:Workflows:MaxSuperSteps", " 7 ")));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value;

        options.RunTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        options.MaxRunTimeout.ShouldBe(TimeSpan.FromDays(1));
        options.MaxSuperSteps.ShouldBe(7);
    }

    [Fact]
    public void Code_that_assigns_every_option_after_binding_wins_over_an_unreadable_value()
    {
        // The failure holds only while the option still has what the binder left. Each key reads its own
        // option: a getter copied from the neighbouring read call would keep the failure alive here.
        var failures = new List<string>();

        foreach (var property in Scalars())
        {
            var key = KeyOf(property);
            var assigned = Changed(property, property.GetValue(new TraconWorkflowOptions()));

            using var provider = Compose(
                Settings((key, Unreadable)),
                options => property.SetValue(options, assigned));

            if (StartupFailure(provider) is { } failure)
            {
                failures.Add($"{key}: the host stopped although code assigned the option after binding: {failure.Message}");

                continue;
            }

            var bound = property.GetValue(provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value);

            if (!Equals(bound, assigned))
            {
                failures.Add($"{key}: the option holds '{bound}', not the value code assigned ('{assigned}').");
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    private static string KeyOf(PropertyInfo property)
        => $"{TraconWorkflowOptions.SectionName}:{property.Name}";

    private static List<PropertyInfo> SettableProperties()
        => typeof(TraconWorkflowOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0 && property.SetMethod is { IsPublic: true })
            .ToList();

    private static List<PropertyInfo> Scalars()
        => SettableProperties().Where(static property => IsReadScalar(property.PropertyType)).ToList();

    private static bool IsReadScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(bool) || underlying == typeof(int) || underlying == typeof(TimeSpan);
    }

    /// <summary>Returns a value that differs from <paramref name="current"/> and passes the options' own validation.</summary>
    private static object Changed(PropertyInfo property, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object changed;

        if (underlying == typeof(bool))
        {
            changed = current is not true;
        }
        else if (underlying == typeof(int))
        {
            changed = (current is int number ? number : 0) + 1;
        }
        else if (underlying == typeof(TimeSpan))
        {
            // The ceiling may not sit below the default run timeout (ten minutes).
            changed = string.Equals(property.Name, nameof(TraconWorkflowOptions.MaxRunTimeout), StringComparison.Ordinal)
                ? TimeSpan.FromHours(2)
                : (current is TimeSpan span ? span : TimeSpan.Zero) + TimeSpan.FromSeconds(7);
        }
        else
        {
            throw new NotSupportedException($"{nameof(TraconWorkflowOptions)} has a scalar of type {underlying.Name}: teach {nameof(Changed)} to change it.");
        }

        return changed;
    }

    private static Dictionary<string, string?> Settings(params (string Key, string Text)[] entries)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, text) in entries)
        {
            values[key] = text;
        }

        return values;
    }

    /// <summary>
    /// Composes the setup through the REAL registration path: <c>AddTracon().UseWorkflows()</c> over the container's
    /// configuration, the way <see cref="WorkflowOptionsConfigurationBindingTests"/> does.
    /// </summary>
    private static ServiceProvider Compose(
        IReadOnlyDictionary<string, string?> values,
        Action<TraconWorkflowOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows(configure);

        return services.BuildServiceProvider();
    }

    /// <summary>Runs the startup validation the host runs and returns its options failure, or <see langword="null"/> when the host starts.</summary>
    private static OptionsValidationException? StartupFailure(IServiceProvider provider)
    {
        try
        {
            provider.GetRequiredService<IStartupValidator>().Validate();

            return null;
        }
        catch (OptionsValidationException exception)
        {
            return exception;
        }
    }
}
