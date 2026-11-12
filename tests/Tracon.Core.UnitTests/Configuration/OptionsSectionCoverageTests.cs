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
    /// <summary>
    /// The root section itself: <see cref="TraconOptionsBindingCoverageTests"/>
    /// walks its whole tree.
    /// </summary>
    private const string RootSection = "TraconOptions";

    [Fact]
    public void No_configuration_section_loses_its_binding_test()
    {
        var sections = SectionTypes().ToList();

        // A scan that found nothing would pass for the wrong reason.
        sections.Count.ShouldBeGreaterThan(20);

        var uncovered = UncoveredSections(sections, typeof(OptionsSectionCoverageTests).Assembly.GetTypes());

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var name in uncovered.Except(baseline, StringComparer.Ordinal))
        {
            failures.Add(
                $"+ {name}: a new configuration section bound by hand, with nothing proving every scalar " +
                $"reaches it. Add a '{name}BindingTests' class in this namespace.");
        }

        foreach (var name in baseline.Except(uncovered, StringComparer.Ordinal))
        {
            failures.Add($"- {name}: now has a binding test - remove it from the baseline.");
        }

        failures.Sort(StringComparer.Ordinal);

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
            UncoveredSections([section], [bindingTest]).Contains(section.Name, StringComparer.Ordinal)
                .ShouldBeTrue($"a class with {shape} took the section off the list");
        }

        // The control: the same shape with a runnable test that calls the key coverage counts.
        UncoveredSections([section], Emit(name, [Test(callsKeyCoverage: true)])).ShouldBeEmpty();
    }

    [Fact]
    public void A_binding_test_counts_only_when_it_can_run_and_names_every_member()
    {
        CountsAsBindingTest(Emit("Runnable", [Test(callsKeyCoverage: true)]).Single()).ShouldBeTrue();
        CountsAsBindingTest(Emit("Skipped", [Test(skip: "later", callsKeyCoverage: true)]).Single()).ShouldBeFalse();
        CountsAsBindingTest(Emit("Explicit", [Test(explicitOnly: true, callsKeyCoverage: true)]).Single()).ShouldBeFalse();
        CountsAsBindingTest(Emit("Unrelated", [Test(callsKeyCoverage: false)]).Single()).ShouldBeFalse();
        CountsAsBindingTest(Emit("Empty", []).Single()).ShouldBeFalse();

        // One runnable test that calls it is enough, whatever else the class holds.
        CountsAsBindingTest(Emit("Mixed", [Test(skip: "later", callsKeyCoverage: false), Test(callsKeyCoverage: true)]).Single())
            .ShouldBeTrue();

        // Real classes of this namespace stand in for the shapes a private nested class cannot take (xUnit1000).
        CountsAsBindingTest(typeof(TraconQuotaOptionsBindingTests)).ShouldBeTrue();
        CountsAsBindingTest(typeof(TraconRunRecordingOptionsBindingTests)).ShouldBeFalse();

        // Measured in xunit.v3 3.2.2 and held in 4.0.2: TheoryAttribute derives from FactAttribute,
        // so one check counts a [Theory]-only class too. If that ever changes,
        // this line fails before a [Theory]-only binding test class goes uncounted.
        typeof(TheoryAttribute).IsSubclassOf(typeof(FactAttribute)).ShouldBeTrue();
    }

    /// <summary>The sections that are neither the root nor a node of its tree and have no counting binding test.</summary>
    /// <param name="sections">The section types.</param>
    /// <param name="candidates">The types that may be a section's binding test class.</param>
    internal static HashSet<string> UncoveredSections(IEnumerable<Type> sections, IEnumerable<Type> candidates)
    {
        var testClassNames = candidates
            .Where(CountsAsBindingTest)
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        return sections
            .Where(type => !string.Equals(type.Name, RootSection, StringComparison.Ordinal))
            .Where(type => !ReachableFromTraconOptions(type))
            .Where(type => !testClassNames.Contains($"{type.Name}BindingTests"))
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether a type in this assembly counts as the binding test its name promises.</summary>
    /// <remarks>
    /// <para>
    /// The name alone used to be enough: an empty <c>{Type}BindingTests</c> class
    /// took a section off the baseline while proving nothing. A class counts only
    /// when one of its tests can run (not skipped, not explicit) AND the class calls
    /// <see cref="OptionsKeyCoverage.ShouldCoverEveryMember"/>: an unrelated test in a
    /// class of the right name proves nothing about the section, and the key
    /// coverage is what names every member.
    /// </para>
    /// <para>
    /// The call is found in the compiled code, not in the source: the method of the
    /// test, and the compiler's own nested types (closures, async state machines) of
    /// the class, are read for a call to the helper. A class cannot satisfy the gate
    /// by mentioning the helper in a comment or a string.
    /// </para>
    /// </remarks>
    internal static bool CountsAsBindingTest(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        var runnable = methods.Where(static method => method.GetCustomAttributes<FactAttribute>(inherit: true)
            .Any(static attribute => string.IsNullOrEmpty(attribute.Skip) && !attribute.Explicit))
            .ToList();

        if (runnable.Count == 0)
        {
            return false;
        }

        var generated = type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .Where(static nested => nested.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false))
            .SelectMany(static nested => nested.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));

        return runnable.Concat(generated).Any(CallsKeyCoverage);
    }

    private static bool CallsKeyCoverage(MethodBase method)
        => CalledMethods(method).Any(static called => called.DeclaringType == typeof(OptionsKeyCoverage)
            && string.Equals(called.Name, nameof(OptionsKeyCoverage.ShouldCoverEveryMember), StringComparison.Ordinal));

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static field => field.FieldType == typeof(OpCode))
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToDictionary(static opCode => opCode.Value);

    /// <summary>Every method the body of <paramref name="method"/> calls, read from its IL.</summary>
    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            yield break;
        }

        for (var position = 0; position < il.Length;)
        {
            var code = il[position] == 0xFE ? (short)(0xFE00 | il[position + 1]) : il[position];
            position += il[position] == 0xFE ? 2 : 1;
            var opCode = OpCodesByValue[code];

            if (opCode.OperandType == OperandType.InlineMethod
                && ResolveMethod(method, BitConverter.ToInt32(il, position)) is { } called)
            {
                yield return called;
            }

            position += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
                _ => 4,
            };
        }
    }

    private static MethodBase? ResolveMethod(MethodBase caller, int token)
    {
        try
        {
            return caller.Module.ResolveMethod(
                token,
                caller.DeclaringType?.IsGenericType == true ? caller.DeclaringType.GetGenericArguments() : null,
                caller.IsGenericMethod ? caller.GetGenericArguments() : null);
        }
        catch (ArgumentException)
        {
            return null;
        }
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

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <remarks>
    /// Read from the repository tree, not from the build output: the file is
    /// edited by hand and a stale copy in <c>bin/</c> would let a removed entry
    /// keep passing. Same pattern as the other baseline gates. A static field
    /// initialiser runs in DECLARATION order, so the root is declared first.
    /// </remarks>
    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot, "tests", "Tracon.Core.UnitTests", "Configuration",
        "options-section-coverage-baseline.txt");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Tracon.slnx not found.");
    }

    private static HashSet<string> ReadBaseline()
        => File.ReadAllLines(BaselinePath)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

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

    /// <summary>
    /// Whether the type is a node of the <c>TraconOptions</c> tree, which
    /// <see cref="TraconOptionsBindingCoverageTests"/> already walks.
    /// </summary>
    private static bool ReachableFromTraconOptions(Type target)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(typeof(TraconOptions));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var type = property.PropertyType;

                if (type == target)
                {
                    return true;
                }

                if (type.IsClass && type.Namespace?.StartsWith("Tracon", StringComparison.Ordinal) == true)
                {
                    queue.Enqueue(type);
                }
            }
        }

        return false;
    }
}
