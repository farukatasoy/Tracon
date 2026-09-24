using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// Three types carry the same tool settings: <see cref="ToolRegistrationOptions"/>
/// (what <c>AddTool</c> configures), <see cref="TraconToolAttribute"/> (what a
/// scanned or generated tool declares) and <see cref="TraconToolRegistration"/>
/// (what the registry reads). A setting that exists on one and is dropped on
/// the way to another is silent, so these tests are driven by reflection: a
/// property that is neither mapped nor excluded by name below fails.
/// </summary>
/// <remarks>
/// The source generator's half of the same table is
/// <c>GeneratedRegistrationParityTests</c> in the generator test project.
/// </remarks>
public sealed class ToolRegistrationParityTests
{
    // Attribute property -> registration property.
    private static readonly Dictionary<string, string> AttributeToRegistration = new(StringComparer.Ordinal)
    {
        ["RequiresApproval"] = "RequiresApproval",
        ["Effect"] = "Effect",
        ["RequiredPermission"] = "RequiredPermission",
        ["TimeoutSeconds"] = "Timeout",
        ["SafeToRepeat"] = "SafeToRepeat",
        ["MaxOutputBytes"] = "MaxOutputBytes",
    };

    // Function metadata that shapes the AIFunction, not the registration.
    private static readonly string[] AttributeExcluded = ["Name", "Description", "JsonSerializerContext"];

    // The constructor argument.
    private const string FunctionProperty = "Function";

    // Only an MCP tool has a source; the attribute has no counterpart.
    private const string SourceProperty = "Source";

    [Fact]
    public void Options_carry_exactly_the_registration_settings_with_the_same_types()
    {
        var options = Properties(typeof(ToolRegistrationOptions)).ToDictionary(static p => p.Name, static p => p.PropertyType, StringComparer.Ordinal);
        var settings = Settings().ToDictionary(static p => p.Name, static p => p.PropertyType, StringComparer.Ordinal);

        options.ShouldBe(settings, ignoreOrder: true);
    }

    [Fact]
    public void Every_attribute_property_is_mapped_to_a_registration_setting_or_excluded()
    {
        var registration = Settings().Select(static p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var property in Properties(typeof(TraconToolAttribute)))
        {
            if (AttributeExcluded.Contains(property.Name))
            {
                continue;
            }

            AttributeToRegistration.ShouldContainKey(property.Name, $"[TraconTool].{property.Name} is neither mapped nor excluded.");
            registration.Contains(AttributeToRegistration[property.Name]).ShouldBeTrue();
        }

        Settings().Select(static p => p.Name)
            .Where(static name => !string.Equals(name, SourceProperty, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ShouldBe(AttributeToRegistration.Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registration_settings_are_init_only_and_the_function_cannot_be_set()
    {
        // A registration is a shared singleton; an ordinary setter would let one
        // caller change the tool for every run.
        foreach (var setting in Settings())
        {
            var setter = setting.SetMethod.ShouldNotBeNull($"'{setting.Name}' has no setter.");
            setter.ReturnParameter.GetRequiredCustomModifiers()
                .ShouldContain(typeof(IsExternalInit), $"'{setting.Name}' must be init-only.");
        }

        typeof(TraconToolRegistration).GetProperty(FunctionProperty)!.SetMethod.ShouldBeNull();
    }

    [Fact]
    public void The_constructor_rejects_a_missing_tool()
    {
        var exception = Should.Throw<ArgumentNullException>(() => new TraconToolRegistration(null!));

        exception.ParamName.ShouldBe("function");
    }

    [Fact]
    public void The_constructor_rejects_an_invalid_tool_name_before_any_setting_is_applied()
    {
        // The name check stays in the constructor: an init property cannot
        // skip it, and no setting can be applied to an invalid registration.
        var exception = Should.Throw<TraconException>(
            () => new TraconToolRegistration(AIFunctionFactory.Create(() => "ok", "has space")) { RequiresApproval = true });

        exception.Message.ShouldContain("has space", Case.Sensitive);
    }

    [Fact]
    public void The_options_mapping_carries_every_setting()
    {
        var options = new ToolRegistrationOptions();
        foreach (var property in Properties(typeof(ToolRegistrationOptions)))
        {
            property.SetValue(options, NonDefaultValue(property.PropertyType));
        }

        var registration = ToolRegistrationMapping.FromOptions(AIFunctionFactory.Create(() => "ok", "parity_tool"), options);

        foreach (var property in Properties(typeof(ToolRegistrationOptions)))
        {
            typeof(TraconToolRegistration).GetProperty(property.Name)!.GetValue(registration)
                .ShouldBe(property.GetValue(options), $"'{property.Name}' did not carry from the options into the registration.");
        }
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test code; runs on the JIT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test code; runs on the JIT.")]
    public void The_scanner_carries_every_mapped_attribute_setting()
    {
        var method = typeof(ParityTools).GetMethod(nameof(ParityTools.Run))!;
        var declared = method.GetCustomAttributesData().Single(static a => a.AttributeType == typeof(TraconToolAttribute))
            .NamedArguments.Select(static a => a.MemberName);
        declared.Order(StringComparer.Ordinal).ShouldBe(
            AttributeToRegistration.Keys.Order(StringComparer.Ordinal),
            "The test tool must set every mapped attribute property.");

        var attribute = method.GetCustomAttribute<TraconToolAttribute>()!;
        var registration = ToolMethodScanner.Scan(typeof(ParityTools)).ShouldHaveSingleItem();

        foreach (var (attributeName, registrationName) in AttributeToRegistration)
        {
            var declaredValue = typeof(TraconToolAttribute).GetProperty(attributeName)!.GetValue(attribute);
            object? expected = attributeName switch
            {
                "TimeoutSeconds" => TimeSpan.FromSeconds((int)declaredValue!),
                _ => declaredValue,
            };

            typeof(TraconToolRegistration).GetProperty(registrationName)!.GetValue(registration)
                .ShouldBe(expected, $"[TraconTool].{attributeName} did not carry into {registrationName}.");
        }

        registration.Source.ShouldBeNull();
    }

    private static PropertyInfo[] Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static IEnumerable<PropertyInfo> Settings() =>
        Properties(typeof(TraconToolRegistration)).Where(static p => !string.Equals(p.Name, FunctionProperty, StringComparison.Ordinal));

    private static object NonDefaultValue(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(bool))
        {
            return true;
        }

        if (target.IsEnum)
        {
            return Enum.GetValues(target).Cast<object>().First(static v => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0);
        }

        if (target == typeof(string))
        {
            return "x";
        }

        if (target == typeof(TimeSpan))
        {
            return TimeSpan.FromSeconds(1);
        }

        if (target == typeof(int))
        {
            return 4096;
        }

        throw new InvalidOperationException($"No test value for setting type '{type}'. Add one here.");
    }

    private static class ParityTools
    {
        [TraconTool(
            "parity_scanned",
            "Parity.",
            RequiresApproval = true,
            Effect = ToolEffect.Destructive,
            RequiredPermission = "orders.cancel",
            TimeoutSeconds = 7,
            SafeToRepeat = true,
            MaxOutputBytes = 4096)]
        public static string Run() => "ok";
    }
}
