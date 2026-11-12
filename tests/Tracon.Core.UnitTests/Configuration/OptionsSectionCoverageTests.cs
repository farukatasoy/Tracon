using System.Reflection;
using System.Reflection.Emit;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every configuration section Tracon binds is covered by a binding test.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <see cref="TraconOptionsBindingCoverageTests"/> locks out "the field was
/// added but never added to <c>Bind()</c>" - but only inside the
/// <c>TraconOptions</c> TREE. <c>TraconImageOptions</c> is a SIBLING section,
/// so the scanner never saw it: <c>Timeout</c> was added, documented as
/// configurable, and bound nowhere. A live run caught it; no test did.
/// </para>
/// <para>
/// Fixing that one class fixes one class. This is the gate for the SHAPE: a
/// new section either joins the tree the scanner walks, or brings a binding
/// test of its own, or is written down here with a reason. None of the three
/// can be skipped silently.
/// </para>
/// </remarks>
public sealed class OptionsSectionCoverageTests
{
    [Fact]
    public void No_configuration_section_loses_its_binding_test()
    {
        var sections = SectionTypes().ToList();

        // A scan that found nothing would pass for the wrong reason.
        sections.Count.ShouldBeGreaterThan(20);

        var failures = OptionsSectionCoverage.BaselineDifferences(
            sections,
            typeof(OptionsSectionCoverageTests).Assembly.GetTypes(),
            OptionsSectionCoverage.ReadBaseline(BaselinePath),
            "in this namespace");

        failures.ShouldBeEmpty(
            customMessage: $"The options-section coverage baseline is stale.{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void A_section_whose_only_binding_test_class_cannot_prove_a_binding_stays_uncovered()
    {
        // The gate itself, fed a section and a class that carries its name but proves nothing. The predicate alone passing
        // is not enough: the gate has to ask it. Each shape below used to take the section off the list.
        var section = typeof(TraconImageOptions);
        var name = $"{section.Name}BindingTests";

        foreach (var (shape, bindingTest) in new (string, Type)[]
        {
            ("no test method", Emit(name, []).Single()),
            ("a skipped test", Emit(name, [Test(skip: "later", callsKeyCoverage: true)]).Single()),
            ("an explicit test", Emit(name, [Test(explicitOnly: true, callsKeyCoverage: true)]).Single()),
            ("a test that never calls the key coverage", Emit(name, [Test(callsKeyCoverage: false)]).Single()),
        })
        {
            OptionsSectionCoverage.UncoveredSections([section], [bindingTest]).Contains(section.Name, StringComparer.Ordinal)
                .ShouldBeTrue($"a class with {shape} took the section off the list");
        }

        // The control: the same shape with a runnable test that calls the key coverage counts.
        OptionsSectionCoverage.UncoveredSections([section], Emit(name, [Test(callsKeyCoverage: true)])).ShouldBeEmpty();
    }

    [Fact]
    public void A_binding_test_counts_only_when_it_can_run_and_names_every_member()
    {
        OptionsSectionCoverage.CountsAsBindingTest(Emit("Runnable", [Test(callsKeyCoverage: true)]).Single()).ShouldBeTrue();
        OptionsSectionCoverage.CountsAsBindingTest(Emit("Skipped", [Test(skip: "later", callsKeyCoverage: true)]).Single()).ShouldBeFalse();
        OptionsSectionCoverage.CountsAsBindingTest(Emit("Explicit", [Test(explicitOnly: true, callsKeyCoverage: true)]).Single()).ShouldBeFalse();
        OptionsSectionCoverage.CountsAsBindingTest(Emit("Unrelated", [Test(callsKeyCoverage: false)]).Single()).ShouldBeFalse();
        OptionsSectionCoverage.CountsAsBindingTest(Emit("Empty", []).Single()).ShouldBeFalse();

        // One runnable test that calls it is enough, whatever else the class holds.
        OptionsSectionCoverage.CountsAsBindingTest(Emit("Mixed", [Test(skip: "later", callsKeyCoverage: false), Test(callsKeyCoverage: true)]).Single())
            .ShouldBeTrue();

        // Real classes of this namespace stand in for the shapes a private nested class cannot take (xUnit1000).
        OptionsSectionCoverage.CountsAsBindingTest(typeof(TraconQuotaOptionsBindingTests)).ShouldBeTrue();
        OptionsSectionCoverage.CountsAsBindingTest(typeof(TraconRunRecordingOptionsBindingTests)).ShouldBeFalse();

        // Measured in xunit.v3 3.2.2 and held in 4.0.2: TheoryAttribute derives from FactAttribute,
        // so one check counts a [Theory]-only class too. If that ever changes,
        // this line fails before a [Theory]-only binding test class goes uncounted.
        typeof(TheoryAttribute).IsSubclassOf(typeof(FactAttribute)).ShouldBeTrue();
    }

    /// <summary>One method of a type <see cref="Emit"/> builds.</summary>
    private readonly record struct TestShape(string? Skip, bool Explicit, bool CallsKeyCoverage);

    private static TestShape Test(string? skip = null, bool explicitOnly = false, bool callsKeyCoverage = false)
        => new(skip, explicitOnly, callsKeyCoverage);

    /// <summary>
    /// Builds a public class named <paramref name="name"/> with one <c>[Fact]</c> method per shape, in a dynamic assembly.
    /// </summary>
    /// <remarks>
    /// A class that carries <c>[Fact(Skip = ...)]</c> in this assembly would be a skipped test in every run, and a private
    /// nested class cannot carry <c>[Fact]</c> at all (xUnit1000). A dynamic assembly is never scanned by the runner, and the
    /// types in it have exactly the shape the gate reads.
    /// </remarks>
    private static IReadOnlyList<Type> Emit(string name, TestShape[] shapes)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"BindingTestShapes.{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("shapes");
        var type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        var factConstructor = typeof(FactAttribute).GetConstructors().Single(static constructor => constructor.GetParameters().Length == 2);
        var skipProperty = typeof(FactAttribute).GetProperty(nameof(FactAttribute.Skip))!;
        var explicitProperty = typeof(FactAttribute).GetProperty(nameof(FactAttribute.Explicit))!;
        var coverage = typeof(OptionsKeyCoverage).GetMethod(nameof(OptionsKeyCoverage.ShouldCoverEveryMember))!
            .MakeGenericMethod(typeof(ProbeOptions));

        var index = 0;

        foreach (var shape in shapes)
        {
            var method = type.DefineMethod($"Test{index++}", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            var properties = new List<PropertyInfo>();
            var values = new List<object?>();

            if (shape.Skip is not null)
            {
                properties.Add(skipProperty);
                values.Add(shape.Skip);
            }

            if (shape.Explicit)
            {
                properties.Add(explicitProperty);
                values.Add(true);
            }

            // FactAttribute(string? sourceFilePath = null, int sourceLineNumber = -1)
            method.SetCustomAttribute(new CustomAttributeBuilder(
                factConstructor, [null, -1], properties.ToArray(), values.ToArray()));

            var il = method.GetILGenerator();

            if (shape.CallsKeyCoverage)
            {
                // OptionsKeyCoverage.ShouldCoverEveryMember<ProbeOptions>(null, null, null, null)
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, coverage);
            }

            il.Emit(OpCodes.Ret);
        }

        return [type.CreateType()];
    }

    /// <summary>The options type the emitted calls are made generic over.</summary>
    public sealed class ProbeOptions;

    /// <remarks>
    /// Read from the repository tree, not from the build output: the file is
    /// edited by hand and a stale copy in <c>bin/</c> would let a removed entry
    /// keep passing. Same pattern as the other baseline gates.
    /// </remarks>
    private static string BaselinePath { get; } = Path.Combine(
        OptionsSectionCoverage.FindRepositoryRoot(), "tests", "Tracon.Core.UnitTests", "Configuration",
        "options-section-coverage-baseline.txt");

    /// <summary>Every options type that names a <c>Tracon:</c> section.</summary>
    private static IEnumerable<Type> SectionTypes()
        => new[] { typeof(TraconOptions).Assembly, typeof(TraconSchedulingOptions).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsPublic: true })
            .Where(static type => type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)
                is { IsLiteral: true } field
                && field.GetRawConstantValue() is string name
                && name.StartsWith("Tracon", StringComparison.Ordinal))
            .Distinct();
}
