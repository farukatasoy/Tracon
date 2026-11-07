using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins the width of every class constructor that takes more than ten parameters to a checked-in
/// baseline, so a constructor can stay wide or shrink but never grow without a decision.
/// </summary>
/// <remarks>
/// <para>
/// A wide constructor is where a new dependency does the most harm: it passes through the
/// registration, every wrapper that forwards it and every test that builds the type by hand, and the
/// defect it breeds is a parameter that is accepted and never read. The three classes that grew the
/// fastest (the run recorder, the agent compiler and the workflow runner) also took the most fixes in
/// the weeks before this gate was added. The gate does not forbid a wide constructor; it makes the
/// next parameter on one a visible choice instead of an accident.
/// </para>
/// <para>
/// Scope: every <c>class</c> in <c>src/</c> (generated files and build output aside) and the widest of
/// its constructors, a primary constructor included. When a class is partial, its parts are read as
/// one type. A record, a struct and an interface are out of scope: a positional record is a data
/// carrier whose width is the shape of the data, not a wiring decision. A static constructor has no
/// parameters and does not count.
/// </para>
/// <para>
/// The key is <c>&lt;package&gt;:&lt;full type name&gt;</c>, then the width, then a reason that says who
/// builds the type. A class over the limit that is not in the baseline fails; a width above the
/// baseline fails; so does a line whose type is gone, is no longer over the limit, or whose width is
/// below the line. The refresh
/// (<c>TRACON_CTOR_WIDTH_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>) only lowers
/// a width and removes a stale line, so the file can only shrink through it. A new line is added by
/// hand.
/// </para>
/// </remarks>
public sealed class ConstructorWidthBaselineTests
{
    private const string RefreshEnvVar = "TRACON_CTOR_WIDTH_REFRESH";
    private const int WidthLimit = 10;
    private const int MinimumReasonLength = 40;

    // A scan that finds far fewer constructors than the tree holds has stopped reading the tree, and
    // a gate that reads nothing passes. The class constructors under src/ number more than five hundred today.
    private const int MinimumScannedConstructors = 450;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    [Fact]
    public void Class_constructors_over_the_limit_match_the_checked_in_baseline()
    {
        var measured = Measure(out var scannedConstructors);

        scannedConstructors.ShouldBeGreaterThan(
            MinimumScannedConstructors,
            "The scan found too few constructors under src/. It no longer reads the tree.");

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal)
            && File.Exists(BaselinePath))
        {
            Refresh(BaselinePath, measured);
        }

        File.Exists(BaselinePath).ShouldBeTrue($"'{BaselinePath}' is missing.");

        var failures = Evaluate(measured, File.ReadAllLines(BaselinePath));

        failures.ShouldBeEmpty(
            customMessage: $"The constructor width baseline does not match src/.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           "A constructor over the limit takes its dependencies through a context object or a smaller " +
                           "collaborator. A line is added BY HAND (<package>:<type> | <width> | <reason that names who " +
                           $"builds the type, at least {MinimumReasonLength} characters>). A stale line or a width above the " +
                           $"real one is fixed with {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    [Theory]
    [InlineData("public sealed class Sample { public Sample(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k) { } }", 11)]
    [InlineData("public sealed class Sample(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l) { }", 12)]
    [InlineData("public sealed class Sample { public Sample(int a) { } internal Sample(int a, int b, int c) { } }", 3)]
    [InlineData("public sealed class Sample { static Sample() { } }", 0)]
    public void The_widest_constructor_of_a_class_is_measured(string source, int expected)
    {
        var widths = new Dictionary<string, int>(StringComparer.Ordinal);

        Scan(source, string.Empty, widths, out _);

        widths.ShouldContainKey("Sample");
        widths["Sample"].ShouldBe(expected);
    }

    [Theory]
    [InlineData("public sealed record Sample(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l);")]
    [InlineData("public readonly record struct Sample(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l);")]
    [InlineData("public struct Sample { public Sample(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l) { } }")]
    public void A_record_or_a_struct_is_out_of_scope(string source)
    {
        var widths = new Dictionary<string, int>(StringComparer.Ordinal);

        Scan(source, string.Empty, widths, out _);

        widths.ShouldBeEmpty();
    }

    [Fact]
    public void The_parts_of_a_partial_class_are_read_as_one_type_and_a_nested_class_has_its_own_name()
    {
        var widths = new Dictionary<string, int>(StringComparer.Ordinal);

        Scan("namespace Demo; public sealed partial class Sample { public Sample(int a) { } }", string.Empty, widths, out _);
        Scan("namespace Demo; public sealed partial class Sample { internal Sample(int a, int b, int c) { } }", string.Empty, widths, out _);
        Scan("namespace Demo { public class Outer { private sealed class Inner(int a, int b) { } } }", string.Empty, widths, out _);

        widths["Demo.Sample"].ShouldBe(3);
        widths["Demo.Outer.Inner"].ShouldBe(2);
    }

    [Fact]
    public void A_generic_class_and_a_plain_class_of_the_same_name_are_two_types()
    {
        var widths = new Dictionary<string, int>(StringComparer.Ordinal);

        Scan("namespace Demo; public class Box { public Box(int a) { } }", string.Empty, widths, out _);
        Scan("namespace Demo; public class Box<T> { public Box(int a, int b, int c) { } }", string.Empty, widths, out _);

        widths["Demo.Box"].ShouldBe(1);
        widths["Demo.Box`1"].ShouldBe(3);
    }

    [Fact]
    public void A_baseline_that_is_too_low_too_high_stale_or_unexplained_is_reported()
    {
        var measured = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["Pkg:Demo.Grown"] = 14,
            ["Pkg:Demo.Shrunk"] = 11,
            ["Pkg:Demo.Missing"] = 12,
            ["Pkg:Demo.Bare"] = 13,
        };

        var reason = new string('x', MinimumReasonLength);
        var failures = Evaluate(
            measured,
            [
                "# a comment",
                $"Pkg:Demo.Grown | 12 | {reason}",
                $"Pkg:Demo.Shrunk | 13 | {reason}",
                $"Pkg:Demo.Gone | 12 | {reason}",
                $"Pkg:Demo.Bare | 13 | too short",
            ]);

        failures.ShouldContain(static failure => failure.Contains("Pkg:Demo.Grown", StringComparison.Ordinal) && failure.StartsWith('+'));
        failures.ShouldContain(static failure => failure.Contains("Pkg:Demo.Shrunk", StringComparison.Ordinal) && failure.StartsWith('~'));
        failures.ShouldContain(static failure => failure.Contains("Pkg:Demo.Missing", StringComparison.Ordinal) && failure.StartsWith('+'));
        failures.ShouldContain(static failure => failure.Contains("Pkg:Demo.Gone", StringComparison.Ordinal) && failure.StartsWith('-'));
        failures.ShouldContain(static failure => failure.Contains("Pkg:Demo.Bare", StringComparison.Ordinal) && failure.Contains("reason", StringComparison.Ordinal));
    }

    private static SortedDictionary<string, int> Measure(out int scannedConstructors)
    {
        var widths = new Dictionary<string, int>(StringComparer.Ordinal);
        var scanned = 0;
        var srcRoot = Path.Combine(RepoRoot.Path, "src");

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcRoot, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);

            if (segments.Length < 2
                || segments.Skip(1).Any(static segment => segment is "obj" or "bin")
                || file.EndsWith(".g.cs", StringComparison.Ordinal)
                || file.EndsWith(".generated.cs", StringComparison.Ordinal))
            {
                continue;
            }

            Scan(File.ReadAllText(file), segments[0] + ":", widths, out var inFile);
            scanned += inFile;
        }

        scannedConstructors = scanned;

        return new SortedDictionary<string, int>(
            widths.Where(static pair => pair.Value > WidthLimit).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    // Adds the widest constructor of every class in the source to `widths`. A key is `keyPrefix` (the
    // package and a colon, or nothing for a snippet) followed by the full type name.
    private static void Scan(string source, string keyPrefix, Dictionary<string, int> widths, out int constructors)
    {
        constructors = 0;

        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var width = 0;

            if (type.ParameterList is { } primary)
            {
                width = primary.Parameters.Count;
                constructors++;
            }

            foreach (var constructor in type.Members.OfType<ConstructorDeclarationSyntax>())
            {
                if (constructor.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                {
                    continue;
                }

                width = Math.Max(width, constructor.ParameterList.Parameters.Count);
                constructors++;
            }

            var key = keyPrefix + FullName(type);

            widths[key] = widths.TryGetValue(key, out var existing) ? Math.Max(existing, width) : width;
        }
    }

    private static string FullName(ClassDeclarationSyntax type)
    {
        var names = new Stack<string>();

        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case TypeDeclarationSyntax declaration:
                    // A generic class keeps its arity (Foo`1), so Foo and Foo<T> are two keys, as they are two types.
                    names.Push(
                        declaration.TypeParameterList is { Parameters.Count: > 0 } typeParameters
                            ? declaration.Identifier.ValueText + "`" + typeParameters.Parameters.Count.ToString(CultureInfo.InvariantCulture)
                            : declaration.Identifier.ValueText);
                    break;
                case BaseNamespaceDeclarationSyntax @namespace:
                    names.Push(@namespace.Name.ToString());
                    break;
            }
        }

        return string.Join('.', names);
    }

    private static List<string> Evaluate(SortedDictionary<string, int> measured, IEnumerable<string> baselineLines)
    {
        var failures = new List<string>();
        var recorded = new Dictionary<string, (int Width, string Reason)>(StringComparer.Ordinal);

        foreach (var line in baselineLines)
        {
            if (ParseLine(line) is not { } entry)
            {
                continue;
            }

            recorded[entry.Key] = (entry.Width, entry.Reason);
        }

        foreach (var (key, width) in measured)
        {
            if (!recorded.TryGetValue(key, out var entry))
            {
                failures.Add($"+ {key}: {width} parameters, over {WidthLimit} and not in the baseline");
            }
            else if (width > entry.Width)
            {
                failures.Add($"+ {key}: {width} parameters, the baseline allows {entry.Width}");
            }
            else if (width < entry.Width)
            {
                failures.Add($"~ {key}: {width} parameters, the baseline still says {entry.Width}; refresh lowers it");
            }
        }

        foreach (var (key, entry) in recorded)
        {
            if (!measured.ContainsKey(key))
            {
                failures.Add($"- {key}: not over {WidthLimit} parameters any more or gone; refresh removes the line");
            }

            if (entry.Reason.Length < MinimumReasonLength)
            {
                failures.Add($"! {key}: the reason is {entry.Reason.Length} characters, it needs at least {MinimumReasonLength} and has to say who builds the type");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        return failures;
    }

    private static void Refresh(string path, SortedDictionary<string, int> measured)
    {
        var output = new List<string>();

        foreach (var line in File.ReadAllLines(path))
        {
            if (ParseLine(line) is not { } entry)
            {
                output.Add(line);

                continue;
            }

            // Only a lower width or a removal: a refresh never adds a line and never raises a width.
            if (!measured.TryGetValue(entry.Key, out var width))
            {
                continue;
            }

            output.Add(width < entry.Width
                ? $"{entry.Key} | {width.ToString(CultureInfo.InvariantCulture)} | {entry.Reason}"
                : line);
        }

        File.WriteAllLines(path, output);
    }

    private static (string Key, int Width, string Reason)? ParseLine(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed[0] == '#')
        {
            return null;
        }

        var parts = trimmed.Split('|', 3, StringSplitOptions.TrimEntries);

        return parts.Length == 3 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            ? (parts[0], width, parts[2])
            : (parts[0], -1, parts.Length > 1 ? parts[^1] : string.Empty);
    }

    private static string BaselinePath { get; } = Path.Combine(
        RepoRoot.Path,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "constructor-width-baseline.txt");
}
