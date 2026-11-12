using System.Reflection;
using System.Reflection.Emit;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The rule behind the options-section coverage gates: a configuration section is covered when a
/// <c>{Type}BindingTests</c> class can run and calls the key coverage, and the compiled code is what proves it.
/// </summary>
/// <remarks>
/// <para>
/// One rule, two scopes. <c>OptionsSectionCoverageTests</c> in Tracon.Core.UnitTests walks the sections of Core and
/// Abstractions; <c>PackageOptionsSectionCoverageTests</c> in Tracon.PackageOptions.UnitTests walks the sections of the
/// provider, MCP, voice, workflow and SQL packages, which the core test project does not reference. This file is linked
/// into both, so the two cannot grow different ideas of what a binding test is.
/// </para>
/// <para>
/// It names no type of either test project: a scope passes its sections and the classes that may be binding tests.
/// </para>
/// </remarks>
internal static class OptionsSectionCoverage
{
    /// <summary>The root section itself: the whole tree under it is walked by its own coverage test.</summary>
    private const string RootSection = "TraconOptions";

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

    /// <summary>The repository root, found from the test binaries.</summary>
    /// <returns>The directory that holds <c>Tracon.slnx</c>.</returns>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Tracon.slnx not found.");
    }

    /// <summary>Reads a baseline: one section type name per line, <c>#</c> starts a comment.</summary>
    /// <param name="path">The baseline file, read from the repository tree and not from the build output.</param>
    /// <returns>The names in it.</returns>
    /// <remarks>
    /// The file is edited by hand, and a stale copy in <c>bin/</c> would let a removed entry keep passing.
    /// </remarks>
    public static HashSet<string> ReadBaseline(string path)
        => File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Compares the uncovered sections with a baseline and says what differs.</summary>
    /// <param name="sections">The section types of the scope.</param>
    /// <param name="candidates">The types of the scope's test assembly.</param>
    /// <param name="baseline">The names the baseline holds.</param>
    /// <param name="where">Where a missing binding test class goes, for the message.</param>
    /// <returns>One line per difference, empty when the baseline is exact.</returns>
    public static List<string> BaselineDifferences(
        IReadOnlyCollection<Type> sections,
        IEnumerable<Type> candidates,
        HashSet<string> baseline,
        string where)
    {
        var uncovered = UncoveredSections(sections, candidates);
        var failures = new List<string>();

        foreach (var name in uncovered.Except(baseline, StringComparer.Ordinal))
        {
            failures.Add(
                $"+ {name}: a new configuration section bound by hand, with nothing proving every scalar " +
                $"reaches it. Add a '{name}BindingTests' class {where}.");
        }

        foreach (var name in baseline.Except(uncovered, StringComparer.Ordinal))
        {
            failures.Add($"- {name}: now has a binding test - remove it from the baseline.");
        }

        failures.Sort(StringComparer.Ordinal);

        return failures;
    }
}
