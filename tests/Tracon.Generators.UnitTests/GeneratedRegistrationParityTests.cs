using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace Tracon.Generators.UnitTests;

/// <summary>
/// The generator writes a registration for every <c>[TraconTool]</c> method
/// into the CONSUMER's assembly. A setting the generator forgets is silent: a
/// destructive tool would run without its permission or its approval.
/// </summary>
/// <remarks>
/// The test is driven by reflection over <see cref="TraconToolAttribute"/> and
/// <see cref="TraconToolRegistration"/>. A property added to either type that is
/// neither in the mapping nor in the written exclusion list fails here, so an
/// eighth setting cannot be added to the types and forgotten in the generator.
/// </remarks>
public sealed class GeneratedRegistrationParityTests
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

    // Function metadata, not registration settings: they shape the generated
    // AIFunction, not the registration.
    private static readonly string[] AttributeExcluded = ["Name", "Description", "JsonSerializerContext"];

    // Function is the constructor argument; Source is set only for MCP tools,
    // and the attribute has no counterpart.
    private static readonly string[] RegistrationExcluded = ["Function", "Source"];

    [Fact]
    public void Every_attribute_property_is_mapped_or_excluded_by_name()
    {
        var unmapped = AttributeProperties()
            .Select(static p => p.Name)
            .Where(static name => !AttributeToRegistration.ContainsKey(name) && !AttributeExcluded.Contains(name))
            .ToList();

        unmapped.ShouldBeEmpty("A new [TraconTool] property must be carried by the generator or excluded here with a reason.");
    }

    [Fact]
    public void Every_registration_property_is_mapped_or_excluded_by_name()
    {
        var unmapped = RegistrationProperties()
            .Select(static p => p.Name)
            .Where(static name => !AttributeToRegistration.ContainsValue(name) && !RegistrationExcluded.Contains(name))
            .ToList();

        unmapped.ShouldBeEmpty("A new TraconToolRegistration setting must be mapped from [TraconTool] or excluded here with a reason.");
    }

    [Fact]
    public void Every_mapped_setting_reaches_the_generated_registration_and_the_output_compiles()
    {
        var mapped = AttributeProperties().Where(static p => AttributeToRegistration.ContainsKey(p.Name)).ToList();
        var arguments = new StringBuilder();
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in mapped)
        {
            var (literal, value) = NonDefaultValue(property.PropertyType);
            arguments.Append(", ").Append(property.Name).Append(" = ").Append(literal);
            expected[AttributeToRegistration[property.Name]] = string.Equals(property.Name, "TimeoutSeconds", StringComparison.Ordinal)
                ? TimeSpan.FromSeconds((int)value!)
                : value;
        }

        var source = $$"""
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("parity_tool", "Parity."{{arguments}})]
                public static string Run() => "ok";
            }
            """;

        foreach (var property in mapped)
        {
            source.ShouldContain(property.Name + " = ", customMessage: "The input must set every mapped attribute property.");
        }

        var result = GeneratorTestHelper.Run(source);
        result.Diagnostics.ShouldBeEmpty();

        var aggregate = result.GeneratedFiles()["TraconGeneratedTools.g.cs"];
        foreach (var name in AttributeToRegistration.Values)
        {
            aggregate.ShouldContain(name + " = ", customMessage: $"The generated registration does not write '{name}'.");
        }

        var registration = CreateGeneratedRegistrations(result).ShouldHaveSingleItem();
        foreach (var (name, value) in expected)
        {
            typeof(TraconToolRegistration).GetProperty(name)!.GetValue(registration)
                .ShouldBe(value, $"'{name}' did not carry from [TraconTool] into the generated registration.");
        }

        registration.Source.ShouldBeNull();
    }

    private static PropertyInfo[] AttributeProperties() =>
        typeof(TraconToolAttribute).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static PropertyInfo[] RegistrationProperties() =>
        typeof(TraconToolRegistration).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static (string Literal, object? Value) NonDefaultValue(Type type)
    {
        if (type == typeof(bool))
        {
            return ("true", true);
        }

        if (type.IsEnum)
        {
            var value = Enum.GetValues(type).Cast<object>().First(static v => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0);
            return ($"{type.Name}.{value}", value);
        }

        if (type == typeof(string))
        {
            return ("\"x\"", "x");
        }

        if (type == typeof(int))
        {
            return ("4096", 4096);
        }

        throw new InvalidOperationException($"No test value for attribute property type '{type}'. Add one here.");
    }

    private static IReadOnlyList<TraconToolRegistration> CreateGeneratedRegistrations(GeneratorRunResult result)
    {
        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join('\n', emit.Diagnostics.Select(static d => d.ToString())));

        stream.Position = 0;
        var context = new AssemblyLoadContext(nameof(GeneratedRegistrationParityTests), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var create = assembly.GetType("Tracon.Generated.TraconGeneratedTools", throwOnError: true)!
                .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;

            return (IReadOnlyList<TraconToolRegistration>)create.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
