using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A <c>Tracon:Voice</c> value whose text cannot be read stops the host at startup, and the
/// message names the key and the text (F-572, phase 247).
/// </summary>
/// <remarks>
/// <para>
/// Before the rule, <c>Tracon:Voice:Timeout = "30s"</c> kept the 100-second default and
/// <c>Tracon:Voice:RequireApproval = "evet"</c> kept the voice tools free of approval, both
/// without a word. The binder now reads every scalar through <c>ConfigurationValueReader</c>,
/// and <c>UnreadableConfigurationValidator</c> refuses to start the host.
/// </para>
/// <para>
/// The binder is written by hand. A scalar added to <see cref="VoiceOptions"/> and read with a
/// plain <c>TryParse</c>, or not read at all, would compile and pass the binding tests that write
/// only valid text. The probe walks the public settable scalars of the options type by
/// reflection, so a new scalar is covered without anyone remembering to add a case.
/// </para>
/// <para>
/// Every case composes the container the way a host does (<c>AddTracon().UseVoice(section)</c>)
/// and starts it through <see cref="IStartupValidator"/>. Reading <c>IOptions&lt;T&gt;.Value</c>
/// runs a validator too, so it would not prove that the application refuses to start. Every
/// configuration carries an API key, so the keyless-setup failure of
/// <c>VoiceOptionsValidator</c> never stands in for the failure under test.
/// </para>
/// </remarks>
public sealed class VoiceUnreadableConfigurationTests
{
    private const string Unreadable = "not-readable";

    /// <summary>Public settable properties the probe does not write, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotProbed = new(StringComparer.Ordinal);

    /// <summary>The address the assignment probe writes over an address option.</summary>
    private static readonly Uri ChangedAddress = new("https://voice.example/");

    [Fact]
    public void Every_scalar_stops_the_host_when_its_text_cannot_be_read()
    {
        var scalars = Scalars();

        // A scan that found few properties would pass for the wrong reason.
        scalars.Count.ShouldBeGreaterThanOrEqualTo(5);

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
            customMessage: $"A scalar is read with the skip-and-keep-the-default pattern, or not read at all:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_settable_property_is_a_probed_scalar_a_string_or_named_with_a_reason()
    {
        var unaccounted = SettableProperties()
            .Where(static property => !IsReadScalar(property.PropertyType)
                && property.PropertyType != typeof(string)
                && !NotProbed.ContainsKey(property.Name))
            .Select(static property => $"{property.Name} ({property.PropertyType.Name})")
            .ToList();

        unaccounted.ShouldBeEmpty(
            customMessage: "The probe writes scalars only. A property of another type needs an entry in NotProbed with the " +
                           $"reason the binder does not read it as a scalar: {string.Join(", ", unaccounted)}");
    }

    [Fact]
    public void Every_exemption_names_a_real_property_the_probe_would_not_write()
    {
        var settable = SettableProperties().ToDictionary(static property => property.Name, StringComparer.Ordinal);

        foreach (var exempt in NotProbed.Keys)
        {
            settable.ShouldContainKey(exempt, $"'{exempt}' is exempt, but {nameof(VoiceOptions)} has no public settable property of that name.");
            IsReadScalar(settable[exempt].PropertyType).ShouldBeFalse($"'{exempt}' is a scalar: it must be probed, not exempt.");
        }
    }

    [Theory]
    [InlineData("Tracon:Voice:Timeout", "30s", "a time span")]
    [InlineData("Tracon:Voice:MaxCharactersPerRequest", "10MB", "a 32-bit whole number")]
    [InlineData("Tracon:Voice:RequireApproval", "evet", "true or false")]
    [InlineData("Tracon:Voice:Endpoint", "api.elevenlabs.io/v1", "an absolute address")]
    public void An_unreadable_value_stops_the_host_and_is_named(string key, string text, string expected)
    {
        using var provider = Compose(Settings((key, text)));

        var failure = StartupFailure(provider);

        failure.ShouldNotBeNull();
        failure.Message.ShouldContain($"{key} is '{text}'");
        failure.Message.ShouldContain(expected);
        failure.Message.ShouldContain("remove the key to keep the default");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_blank_value_means_not_given_and_keeps_the_default(string text)
    {
        var values = Settings();

        foreach (var property in Scalars())
        {
            values[KeyOf(property)] = text;
        }

        using var provider = Compose(values);

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<VoiceOptions>>().Value;
        var defaults = new VoiceOptions();

        foreach (var property in Scalars())
        {
            property.GetValue(options).ShouldBe(property.GetValue(defaults), $"{property.Name} must keep its default.");
        }
    }

    [Fact]
    public void Readable_text_binds_and_the_host_starts()
    {
        using var provider = Compose(Settings(
            ("Tracon:Voice:Timeout", "00:00:45"),
            ("Tracon:Voice:MaxConcurrentRequests", " 4 "),
            ("Tracon:Voice:RequireApproval", "true")));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<VoiceOptions>>().Value;

        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        options.MaxConcurrentRequests.ShouldBe(4);
        options.RequireApproval.ShouldBeTrue();
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
            var assigned = property.PropertyType == typeof(Uri)
                ? ChangedAddress
                : Changed(property.PropertyType, property.GetValue(new VoiceOptions()));

            using var provider = Compose(
                Settings((key, Unreadable)),
                options => property.SetValue(options, assigned));

            if (StartupFailure(provider) is { } failure)
            {
                failures.Add($"{key}: the host stopped although code assigned the option after binding: {failure.Message}");

                continue;
            }

            var bound = property.GetValue(provider.GetRequiredService<IOptions<VoiceOptions>>().Value);

            if (!Equals(bound, assigned))
            {
                failures.Add($"{key}: the option holds '{bound}', not the value code assigned ('{assigned}').");
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    private static string KeyOf(PropertyInfo property)
        => $"{VoiceOptions.SectionName}:{property.Name}";

    private static List<PropertyInfo> SettableProperties()
        => typeof(VoiceOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0 && property.SetMethod is { IsPublic: true })
            .ToList();

    private static List<PropertyInfo> Scalars()
        => SettableProperties().Where(static property => IsReadScalar(property.PropertyType)).ToList();

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
            || underlying == typeof(Uri)
            || underlying.IsEnum;
    }

    /// <summary>Returns a value that differs from <paramref name="current"/> and passes the options' own validation.</summary>
    private static object Changed(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return current is not true;
        }

        if (underlying == typeof(int))
        {
            return (current is int number ? number : 0) + 1;
        }

        if (underlying == typeof(TimeSpan))
        {
            return (current is TimeSpan span ? span : TimeSpan.Zero) + TimeSpan.FromSeconds(7);
        }

        throw new NotSupportedException($"{nameof(VoiceOptions)} has a scalar of type {underlying.Name}: teach {nameof(Changed)} to change it.");
    }

    /// <summary>The configuration of a keyed setup: the API key (a short fake value) plus the given entries.</summary>
    private static Dictionary<string, string?> Settings(params (string Key, string Text)[] entries)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Voice:ApiKey"] = "k",
        };

        foreach (var (key, text) in entries)
        {
            values[key] = text;
        }

        return values;
    }

    /// <summary>
    /// Composes the setup through the REAL registration path: <c>AddTracon().UseVoice(section)</c>, the way
    /// <c>VoiceDialogueRegistrationTests</c> reads its settings from configuration.
    /// </summary>
    private static ServiceProvider Compose(
        IReadOnlyDictionary<string, string?> values,
        Action<VoiceOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();

        services.AddTracon()
            .UseVoice(configuration.GetSection(VoiceOptions.SectionName), configure);

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
