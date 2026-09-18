using System.Reflection;

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

        var testClassNames = typeof(OptionsSectionCoverageTests).Assembly.GetTypes()
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = sections
            .Where(type => !string.Equals(type.Name, RootSection, StringComparison.Ordinal))
            .Where(type => !ReachableFromTraconOptions(type))
            .Where(type => !testClassNames.Contains($"{type.Name}BindingTests"))
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

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
