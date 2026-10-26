using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Text under <c>Tracon:PostgreSql</c>, <c>Tracon:SqlServer</c> or <c>Tracon:Sqlite</c> that the
/// binder cannot read stops the host at startup, and the failure names the key and the text (F-572).
/// </summary>
/// <typeparam name="TOptions">The options type of one SQL provider.</typeparam>
/// <remarks>
/// <para>
/// Before the rule, <c>CommandTimeoutSeconds = "30s"</c> kept the shipped 30 seconds and
/// <c>AutoApplyMigrations = "evet"</c> kept the migrations on, without a word. The three binders
/// are written by hand, so a scalar added to an options type and read with the old
/// skip-and-keep-the-default pattern would compile and pass a test that writes only valid
/// text. The probe walks the scalars of the options type, writes text no type can read into that
/// one key, and expects the host to refuse to start.
/// </para>
/// <para>
/// Every case builds the container a host builds and runs <see cref="IStartupValidator"/>, the
/// startup path. The validators run and no hosted service starts, so no case opens a connection.
/// </para>
/// <para>
/// Deliberately placed OUTSIDE the three <c>*.IntegrationTests</c> projects (see
/// <see cref="SqlTextSnapshotTests"/>): each carries an <c>[assembly: AssemblyFixture(...)]</c>
/// that starts a container (SQLite: a temporary directory) for the whole assembly before any test
/// runs, also for a class that never takes the fixture. Measured on 2026-10-26: running only a
/// class of <c>Tracon.PostgreSql.IntegrationTests</c> that opens no connection still started the
/// Ryuk and the <c>pgvector</c> containers. The behaviour is the same for the three providers, so
/// one base class holds it and each provider names only what differs.
/// </para>
/// </remarks>
public abstract class SqlUnreadableConfigurationTests<TOptions>
    where TOptions : class, new()
{
    private const string Unreadable = "not-readable";

    private const string ConnectionString = "ConnectionString";

    private const string AutoApplyMigrations = "AutoApplyMigrations";

    private const string CommandTimeoutSeconds = "CommandTimeoutSeconds";

    private const string EnableReadViews = "EnableReadViews";

    /// <summary>Settable options that are NOT probed on purpose, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotProbed = new(StringComparer.Ordinal)
    {
        ["DataSource"] = "an object the application hands over in code; configuration has no text for it",
    };

    /// <summary>Gets the full path of the configuration section, such as <c>Tracon:Sqlite</c>.</summary>
    protected abstract string SectionName { get; }

    /// <summary>
    /// Gets a connection string the options validator accepts. It is never opened: only the
    /// validators run. Give no secret.
    /// </summary>
    protected abstract string PlaceholderConnectionString { get; }

    /// <summary>Adds the provider to the chain, reading the given section.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="section">The provider's section.</param>
    protected abstract void Use(ITraconBuilder builder, IConfiguration section);

    [Fact]
    public void Every_scalar_option_stops_the_host_when_its_text_cannot_be_read()
    {
        var scalars = ScalarOptions();

        // A scan that found nothing would pass for the wrong reason.
        scalars.ShouldNotBeEmpty();

        var failures = new List<string>();

        foreach (var scalar in scalars)
        {
            var key = KeyOf(scalar.Name);

            try
            {
                var failure = FailsAtStartup(Values((scalar.Name, Unreadable)));

                if (!failure.Message.Contains($"{key} is '{Unreadable}'", StringComparison.Ordinal))
                {
                    failures.Add($"{key}: the host stopped, but the message does not name the key and the text: {failure.Message}");
                }
            }
            catch (ShouldAssertException)
            {
                failures.Add($"{key}: the host did not stop for text it cannot read.");
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A scalar is read with the skip-and-keep-the-default pattern, or not read at all:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_settable_option_is_a_probed_scalar_text_or_a_named_exemption()
    {
        // A property of another type (a list, a nested object) would be read by a binder the probe cannot see.
        var unclassified = SettableOptions()
            .Where(static property => !IsReadScalar(property.PropertyType)
                && property.PropertyType != typeof(string)
                && !NotProbed.ContainsKey(property.Name))
            .Select(static property => $"{property.Name} ({property.PropertyType.Name})")
            .ToList();

        unclassified.ShouldBeEmpty(
            customMessage: "The probe does not cover these options. Probe them, or list them in NotProbed with the reason.");
    }

    [Fact]
    public void Every_exemption_names_a_real_settable_option()
    {
        var names = SettableOptions().Select(static property => property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var exempt in NotProbed.Keys)
        {
            names.Contains(exempt).ShouldBeTrue(customMessage: $"'{exempt}' is exempt, but no settable option has that name.");
        }
    }

    [Fact]
    public void A_command_timeout_that_is_not_a_number_is_refused_and_named()
    {
        var failure = FailsAtStartup(Values((CommandTimeoutSeconds, "30s")));

        failure.Message.ShouldContain($"{SectionName}:{CommandTimeoutSeconds} is '30s'");
        failure.Message.ShouldContain("32-bit whole number");
    }

    [Fact]
    public void A_migration_flag_that_is_not_true_or_false_is_refused_and_named()
    {
        var failure = FailsAtStartup(Values((AutoApplyMigrations, "evet")));

        failure.Message.ShouldContain($"{SectionName}:{AutoApplyMigrations} is 'evet'");
        failure.Message.ShouldContain("true or false");
    }

    [Fact]
    public void Every_unreadable_value_is_named_in_one_failure()
    {
        // The operator fixes both keys from one start.
        var failure = FailsAtStartup(Values((CommandTimeoutSeconds, "30s"), (AutoApplyMigrations, "evet")));

        failure.Message.ShouldContain($"{SectionName}:{CommandTimeoutSeconds} is '30s'");
        failure.Message.ShouldContain($"{SectionName}:{AutoApplyMigrations} is 'evet'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_blank_text_means_not_given_and_keeps_the_default(string text)
    {
        var scalars = ScalarOptions();

        using var provider = Build(Values([.. scalars.Select(scalar => (scalar.Name, text))]));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var bound = provider.GetRequiredService<IOptions<TOptions>>().Value;
        var defaults = new TOptions();

        foreach (var scalar in scalars)
        {
            scalar.GetValue(bound).ShouldBe(scalar.GetValue(defaults), $"{KeyOf(scalar.Name)} is empty, so the default stays");
        }
    }

    [Fact]
    public void Text_with_surrounding_white_space_is_still_readable()
    {
        using var provider = Build(Values((CommandTimeoutSeconds, " 45 "), (AutoApplyMigrations, " false "), (EnableReadViews, " true ")));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var bound = provider.GetRequiredService<IOptions<TOptions>>().Value;

        Read<int>(bound, CommandTimeoutSeconds).ShouldBe(45);
        Read<bool>(bound, AutoApplyMigrations).ShouldBeFalse();
        Read<bool>(bound, EnableReadViews).ShouldBeTrue();
    }

    [Fact]
    public void A_valid_configuration_starts_cleanly()
    {
        using var provider = Build(Values());

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Code_that_assigns_the_option_after_binding_wins_over_an_unreadable_value()
    {
        using var provider = Build(
            Values((CommandTimeoutSeconds, "30s"), (AutoApplyMigrations, "evet")),
            static services => services.Configure<TOptions>(static options =>
            {
                Assign(options, CommandTimeoutSeconds, 45);
                Assign(options, AutoApplyMigrations, false);
            }));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var bound = provider.GetRequiredService<IOptions<TOptions>>().Value;

        Read<int>(bound, CommandTimeoutSeconds).ShouldBe(45);
        Read<bool>(bound, AutoApplyMigrations).ShouldBeFalse();
    }

    [Fact]
    public void Code_that_assigns_one_option_does_not_hide_another_unreadable_value()
    {
        var failure = FailsAtStartup(
            Values((CommandTimeoutSeconds, "30s"), (AutoApplyMigrations, "evet")),
            static services => services.Configure<TOptions>(static options => Assign(options, CommandTimeoutSeconds, 45)));

        failure.Message.ShouldNotContain(CommandTimeoutSeconds);
        failure.Message.ShouldContain($"{SectionName}:{AutoApplyMigrations} is 'evet'");
    }

    private static List<PropertyInfo> SettableOptions()
        => [.. typeof(TOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0
                && property.GetMethod is { IsPublic: true }
                && property.SetMethod is { IsPublic: true })];

    private static List<PropertyInfo> ScalarOptions()
        => [.. SettableOptions().Where(static property => IsReadScalar(property.PropertyType))];

    private static bool IsReadScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(bool)
            || underlying == typeof(int)
            || underlying == typeof(long)
            || underlying == typeof(float)
            || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying == typeof(TimeSpan)
            || underlying.IsEnum;
    }

    private static void Assign(TOptions options, string name, object value)
        => typeof(TOptions).GetProperty(name)!.SetValue(options, value);

    private static T Read<T>(TOptions options, string name)
        => (T)typeof(TOptions).GetProperty(name)!.GetValue(options)!;

    private string KeyOf(string name) => $"{SectionName}:{name}";

    /// <summary>
    /// A readable configuration that starts the host cleanly, with the given options changed.
    /// </summary>
    /// <remarks>
    /// <c>AutoApplyMigrations</c> is turned off in configuration, not in code: a code assignment after
    /// binding would hide an unreadable <c>AutoApplyMigrations</c> and the probe could not see it.
    /// </remarks>
    private Dictionary<string, string?> Values(params (string Name, string Text)[] entries)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [KeyOf(ConnectionString)] = PlaceholderConnectionString,
            [KeyOf(AutoApplyMigrations)] = "false",
        };

        foreach (var (name, text) in entries)
        {
            values[KeyOf(name)] = text;
        }

        return values;
    }

    private ServiceProvider Build(Dictionary<string, string?> values, Action<IServiceCollection>? afterBinding = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        Use(services.AddTracon(), configuration.GetSection(SectionName));
        afterBinding?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private OptionsValidationException FailsAtStartup(
        Dictionary<string, string?> values,
        Action<IServiceCollection>? afterBinding = null)
    {
        using var provider = Build(values, afterBinding);

        return Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }
}
