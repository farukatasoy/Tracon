using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins every PUBLIC constructor of a concrete class to a checked-in baseline
/// with a reason per entry, so a service type the container builds cannot
/// reach the public surface again unnoticed.
/// </summary>
/// <remarks>
/// <para>
/// Phase 188 made fifteen constructors internal and pinned the ones that take an
/// optional parameter (<see cref="Public_constructors_with_optional_parameters_match_the_checked_in_baseline"/>).
/// That gate cannot see a constructor WITHOUT an optional parameter, and five
/// service types the container builds kept a public one for that reason
/// (<c>SingleTenantContext</c>, <c>HttpTenantContext</c>, <c>TimeoutAIFunction</c>,
/// <c>CompiledAgentCache</c>, <c>DefaultRunAttributionContext</c>): the same
/// defect passed twice. This gate counts every public constructor of a concrete
/// class, so the next one is a decision instead of an accident.
/// </para>
/// <para>
/// Scope: a class that is public, not abstract, not static and not a record, whose
/// name does not end in <c>Options</c>, <c>Settings</c> or <c>Exception</c>. Those
/// three are bound from configuration or thrown by a consumer, so a public
/// constructor is their normal shape. A value type, an interface, an enum and a
/// delegate are out of scope. The kind is read from the declaration in the
/// package's own source, because <c>PublicAPI.Unshipped.txt</c> does not say
/// whether a type is abstract or a struct; a type with no declaration found is
/// kept in scope so the gate never goes quiet by accident.
/// </para>
/// <para>
/// The key is <c>&lt;package&gt;:&lt;full type name&gt;</c>. A new entry is added BY
/// HAND with a reason that names who builds the type; the refresh
/// (<c>TRACON_PUBLIC_CTOR_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>)
/// only removes stale entries, so the baseline can only shrink through it. A type
/// the container or the run pipeline builds takes an <c>internal</c> constructor
/// and a factory registration instead.
/// </para>
/// </remarks>
public sealed partial class PublicSurfaceBaselineTests
{
    private const string ConstructorRefreshEnvVar = "TRACON_PUBLIC_CTOR_REFRESH";

    private static readonly string[] ConfigurationBoundSuffixes = ["Options", "Settings", "Exception"];

    private static readonly Regex DeclarationPattern = new(
        @"^[ \t]*public[ \t]+(?<modifiers>(?:(?:sealed|abstract|static|partial|unsafe|readonly|ref|file|new)[ \t]+)*)" +
        @"(?<kind>record[ \t]+struct|record[ \t]+class|record|class|struct|interface|enum)[ \t]+(?<name>\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DelegateDeclarationPattern = new(
        @"^[ \t]*public[ \t]+delegate[ \t]+[^(;=]*?[ \t](?<name>\w+)[ \t]*(?:<[^>(]*>)?[ \t]*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex RecordOwnerPattern = new(
        @"^~?(?:[a-z]+\s+)*(?<owner>.+)\.<Clone>\$\(\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Public_constructors_of_concrete_classes_match_the_checked_in_baseline()
    {
        var actual = MeasureConcreteClassConstructors();

        if (string.Equals(Environment.GetEnvironmentVariable(ConstructorRefreshEnvVar), "1", StringComparison.Ordinal)
            && File.Exists(ConstructorBaselinePath))
        {
            RemoveStaleConstructorEntries(ConstructorBaselinePath, actual);
        }

        var baselineLines = File.Exists(ConstructorBaselinePath)
            ? File.ReadAllLines(ConstructorBaselinePath)
            : null;

        var failures = EvaluateConstructorBaseline(
            actual,
            baselineLines,
            ConstructorBaselinePath,
            "<package>:<type> | <reason>",
            "new public constructor on a concrete class");

        failures.ShouldBeEmpty(
            customMessage: $"The public constructor baseline does not match the public API.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           "A new entry needs a public API decision: make the constructor internal and register the " +
                           "type with a factory, or add the line BY HAND with a reason of at least " +
                           $"{MinimumReasonLength} characters that names who builds the type. A stale entry is removed with " +
                           $"{ConstructorRefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    [Theory]
    [InlineData("public sealed class Sample", "class")]
    [InlineData("    public sealed partial class Sample : Base", "class")]
    [InlineData("public abstract class Sample", "abstract")]
    [InlineData("public static class Sample", "abstract")]
    [InlineData("public sealed record Sample(int Value);", "other")]
    [InlineData("public readonly record struct Sample(int Value);", "other")]
    [InlineData("public readonly struct Sample", "other")]
    [InlineData("public interface Sample", "other")]
    [InlineData("public enum Sample", "other")]
    [InlineData("public delegate ValueTask Sample(int value);", "other")]
    [InlineData("public delegate TResult Sample<TArg, TResult>(TArg value);", "other")]
    [InlineData("internal sealed class Sample", "none")]
    public void Declaration_scan_reads_the_kind_of_a_type(string declaration, string expected)
    {
        var declarations = ScanDeclarations(declaration + Environment.NewLine + "{" + Environment.NewLine + "}");

        var actual = declarations.TryGetValue("Sample", out var kinds)
            ? kinds.Single()
            : "none";

        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Tracon.Sample.<Clone>$() -> Tracon.Sample!", "Tracon.Sample")]
    [InlineData("~Tracon.Sample<T>.<Clone>$() -> Tracon.Sample<T>!", "Tracon.Sample<T>")]
    [InlineData("Tracon.Sample.Value.get -> int", null)]
    public void Record_owner_is_read_from_the_clone_member(string line, string? expected)
    {
        var match = RecordOwnerPattern.Match(line);

        (match.Success ? match.Groups["owner"].Value : null).ShouldBe(expected);
    }

    private static SortedSet<string> MeasureConcreteClassConstructors()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var unshippedFile in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"), "PublicAPI.Unshipped.txt", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(unshippedFile)!;
            var package = Path.GetFileName(directory);
            var shippedFile = Path.Combine(directory, "PublicAPI.Shipped.txt");
            var shipped = File.Exists(shippedFile) ? File.ReadLines(shippedFile) : [];
            var active = ActiveApiLines(shipped, File.ReadLines(unshippedFile));

            var records = active
                .Select(static line => RecordOwnerPattern.Match(line))
                .Where(static match => match.Success)
                .Select(static match => match.Groups["owner"].Value)
                .ToHashSet(StringComparer.Ordinal);

            Dictionary<string, List<string>>? declarations = null;

            foreach (var line in active)
            {
                if (ParseConstructor(line) is not { } constructor)
                {
                    continue;
                }

                var simpleName = constructor.TypeName[(constructor.TypeName.LastIndexOf('.') + 1)..];
                var genericStart = simpleName.IndexOf('<', StringComparison.Ordinal);
                var bareName = genericStart < 0 ? simpleName : simpleName[..genericStart];

                if (ConfigurationBoundSuffixes.Any(suffix => bareName.EndsWith(suffix, StringComparison.Ordinal))
                    || records.Contains(constructor.TypeName))
                {
                    continue;
                }

                declarations ??= ScanPackageDeclarations(directory);

                // No declaration found: stay in scope. A gate that cannot classify a type
                // must ask for a reason, not wave it through.
                if (!declarations.TryGetValue(bareName, out var kinds) || kinds.Contains("class", StringComparer.Ordinal))
                {
                    keys.Add($"{package}:{constructor.TypeName}");
                }
            }
        }

        return keys;
    }

    private static Dictionary<string, List<string>> ScanPackageDeclarations(string packageDirectory)
    {
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(packageDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(packageDirectory, file);

            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (name, kind) in ScanDeclarations(File.ReadAllText(file)))
            {
                if (!declarations.TryGetValue(name, out var kinds))
                {
                    kinds = [];
                    declarations[name] = kinds;
                }

                kinds.AddRange(kind);
            }
        }

        return declarations;
    }

    // "class" is a concrete class (the only kind in scope), "abstract" an abstract or static
    // class, "other" a record, struct, interface, enum or delegate.
    private static Dictionary<string, List<string>> ScanDeclarations(string source)
    {
        var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (Match match in DeclarationPattern.Matches(source))
        {
            var kind = match.Groups["kind"].Value;
            var modifiers = match.Groups["modifiers"].Value;

            var classification = string.Equals(kind, "class", StringComparison.Ordinal)
                ? modifiers.Contains("abstract", StringComparison.Ordinal) || modifiers.Contains("static", StringComparison.Ordinal)
                    ? "abstract"
                    : "class"
                : "other";

            Add(declarations, match.Groups["name"].Value, classification);
        }

        foreach (Match match in DelegateDeclarationPattern.Matches(source))
        {
            Add(declarations, match.Groups["name"].Value, "other");
        }

        return declarations;

        static void Add(Dictionary<string, List<string>> map, string name, string classification)
        {
            if (!map.TryGetValue(name, out var kinds))
            {
                kinds = [];
                map[name] = kinds;
            }

            kinds.Add(classification);
        }
    }

    // Computed, not initialised: a static initialiser here would read RepositoryRoot, which the
    // other part of this class initialises, and partial parts have no defined initialisation order.
    private static string ConstructorBaselinePath => Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "public-constructor-baseline.txt");
}
