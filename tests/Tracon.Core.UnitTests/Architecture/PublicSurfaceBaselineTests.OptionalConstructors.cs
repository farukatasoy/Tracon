using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins every PUBLIC constructor that takes an optional parameter to a
/// checked-in baseline with a reason per entry, so the class Phase 188 closed
/// cannot regrow silently.
/// </summary>
/// <remarks>
/// <para>
/// A public constructor with optional parameters freezes at <c>1.0.0</c>: the
/// only non-breaking way to add a dependency afterwards is one more overload,
/// and the analyzer's RS0026/RS0027 rules make each overload a design exercise.
/// Phase 188 made the constructors of the fifteen service types the container
/// or the run pipeline builds <c>internal</c>; the entries left in
/// <c>optional-parameter-constructor-baseline.txt</c> are types a consumer
/// builds itself, each with a reason.
/// </para>
/// <para>
/// The key carries the parameter counts: adding an optional parameter to a
/// constructor that is already in the baseline changes its key, which the gate
/// reports as one new and one stale entry. A new entry is added BY HAND with a
/// reason that names the decision; the refresh
/// (<c>TRACON_OPTIONAL_CTOR_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>)
/// only removes stale entries, so the baseline can only shrink through it.
/// </para>
/// <para>
/// Only constructors are counted; methods with optional parameters are out of
/// scope, and a new public constructor WITHOUT optional parameters is not seen.
/// </para>
/// </remarks>
public sealed partial class PublicSurfaceBaselineTests
{
    private const string OptionalConstructorRefreshEnvVar = "TRACON_OPTIONAL_CTOR_REFRESH";
    private const int MinimumReasonLength = 40;
    private const string RemovedPrefix = "*REMOVED*";

    // <optional namespace/containing type>.<Name><optional generic list>.<Name>(<parameters>) -> void
    private static readonly Regex ConstructorPattern = new(
        @"^(?<type>(?:[\w.]+\.)?(?<name>\w+)(?:<[^>]*>)?)\.\k<name>\((?<parameters>.*)\) -> void$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Public_constructors_with_optional_parameters_match_the_checked_in_baseline()
    {
        var actual = MeasureOptionalParameterConstructors();

        if (string.Equals(Environment.GetEnvironmentVariable(OptionalConstructorRefreshEnvVar), "1", StringComparison.Ordinal)
            && File.Exists(OptionalConstructorBaselinePath))
        {
            RemoveStaleOptionalConstructorEntries(actual);
        }

        var baselineLines = File.Exists(OptionalConstructorBaselinePath)
            ? File.ReadAllLines(OptionalConstructorBaselinePath)
            : null;

        var failures = EvaluateOptionalConstructorBaseline(actual, baselineLines);

        failures.ShouldBeEmpty(
            customMessage: $"The optional-parameter constructor baseline does not match the public API.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           "A new entry needs a public API decision: make the constructor internal and resolve the " +
                           "type from DI, or add the line BY HAND with a reason of at least " +
                           $"{MinimumReasonLength} characters. A stale entry is removed with " +
                           $"{OptionalConstructorRefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    [Theory]
    [InlineData(
        "Tracon.AgentRunBudget.AgentRunBudget(System.TimeSpan? maxDuration = null, System.TimeProvider? timeProvider = null) -> void",
        "Tracon.AgentRunBudget(2/2)")]
    [InlineData(
        "Tracon.Testing.Contracts.Storage.StoreCancellationContract<TStore>.StoreCancellationContract() -> void",
        "Tracon.Testing.Contracts.Storage.StoreCancellationContract<TStore>(0/0)")]
    [InlineData(
        "Tracon.Sample.Sample(System.Collections.Generic.IReadOnlyDictionary<string!, string!>! map, int count = 0) -> void",
        "Tracon.Sample(2/1)")]
    [InlineData(
        "Tracon.Sample.Sample(string! id, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> void",
        "Tracon.Sample(2/1)")]
    [InlineData(
        "Tracon.Testing.FakeModelProvider.FakeModelProvider(string! name = \"fake\") -> void",
        "Tracon.Testing.FakeModelProvider(1/1)")]
    [InlineData(
        "Tracon.Sample.Sample(string! separator = \", = (\", int count = 1) -> void",
        "Tracon.Sample(2/2)")]
    [InlineData(
        "~Tracon.Sample.Sample(object value, int count = 1) -> void",
        "Tracon.Sample(2/1)")]
    [InlineData(
        "Tracon.Outer.Inner.Inner(int? value = null) -> void",
        "Tracon.Outer.Inner(1/1)")]
    public void Constructor_parser_reads_the_type_and_the_parameter_counts(string line, string expected)
    {
        var constructor = ParseConstructor(line);

        constructor.ShouldNotBeNull();
        $"{constructor.Value.TypeName}({constructor.Value.Parameters}/{constructor.Value.Optional})".ShouldBe(expected);
    }

    [Theory]
    [InlineData("Tracon.Sample.Run(int count = 0) -> void")]
    [InlineData("Tracon.Sample.Sample.get -> int")]
    [InlineData("static Tracon.Sample.Create(int count = 0) -> Tracon.Sample!")]
    [InlineData("Tracon.Sample")]
    [InlineData("#nullable enable")]
    public void Constructor_parser_ignores_lines_that_are_not_constructors(string line)
    {
        ParseConstructor(line).ShouldBeNull();
    }

    [Fact]
    public void Removed_entry_in_Unshipped_cancels_the_Shipped_constructor()
    {
        const string constructor = "Tracon.Sample.Sample(int count = 0) -> void";

        var active = ActiveApiLines([constructor], [$"{RemovedPrefix}{constructor}"]);

        active.ShouldBeEmpty();
    }

    [Fact]
    public void Gate_reports_a_new_a_stale_a_short_and_a_duplicate_entry()
    {
        var actual = new SortedSet<string>(StringComparer.Ordinal)
        {
            "Tracon.Abstractions:Tracon.Kept(1/1)",
            "Tracon.Abstractions:Tracon.Added(2/1)",
        };
        string[] baseline =
        [
            "# comment",
            "Tracon.Abstractions:Tracon.Kept(1/1) | A consumer builds this type itself, as the site guide shows.",
            "Tracon.Abstractions:Tracon.Kept(1/1) | A consumer builds this type itself, as the site guide shows.",
            "Tracon.Abstractions:Tracon.Gone(3/2) | short",
            "Tracon.Abstractions:Tracon.Malformed(1/1)",
        ];

        var failures = EvaluateOptionalConstructorBaseline(actual, baseline);

        failures.ShouldContain(failure => failure.StartsWith("+ Tracon.Abstractions:Tracon.Added(2/1)", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.StartsWith("- Tracon.Abstractions:Tracon.Gone(3/2)", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("shorter than", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("more than once", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("Malformed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gate_is_red_when_the_baseline_is_missing_or_empty(bool fileExists)
    {
        var actual = new SortedSet<string>(StringComparer.Ordinal) { "Tracon.Abstractions:Tracon.Kept(1/1)" };
        string[]? baseline = fileExists ? ["# only a comment", ""] : null;

        var failures = EvaluateOptionalConstructorBaseline(actual, baseline);

        failures.ShouldNotBeEmpty();
        failures[0].ShouldContain(fileExists ? "has no entries" : "is missing", Case.Sensitive);
    }

    internal static (string TypeName, int Parameters, int Optional)? ParseConstructor(string line)
    {
        var text = line.Trim();

        if (text.StartsWith('~'))
        {
            text = text[1..];
        }

        var match = ConstructorPattern.Match(text);

        if (!match.Success)
        {
            return null;
        }

        var parameters = SplitParameters(match.Groups["parameters"].Value);

        return (match.Groups["type"].Value, parameters.Count, parameters.Count(IsOptionalParameter));
    }

    internal static List<string> EvaluateOptionalConstructorBaseline(IReadOnlySet<string> actual, string[]? baselineLines)
    {
        if (baselineLines is null)
        {
            return [$"'{OptionalConstructorBaselinePath}' is missing - the gate is never silently green."];
        }

        var failures = new List<string>();
        var baseline = new HashSet<string>(StringComparer.Ordinal);
        var entries = 0;

        foreach (var line in baselineLines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            entries++;
            var separator = line.IndexOf(" | ", StringComparison.Ordinal);

            if (separator <= 0)
            {
                failures.Add($"! Malformed line (expected '<package>:<type>(<parameters>/<optional>) | <reason>'): '{line}'");
                continue;
            }

            var key = line[..separator].Trim();
            var reason = line[(separator + 3)..].Trim();

            if (!baseline.Add(key))
            {
                failures.Add($"! {key}: listed more than once");
            }

            if (reason.Length < MinimumReasonLength)
            {
                failures.Add($"! {key}: reason shorter than {MinimumReasonLength} characters: '{reason}'");
            }
        }

        if (entries == 0)
        {
            return [$"'{OptionalConstructorBaselinePath}' has no entries - the gate is never silently green."];
        }

        failures.AddRange(actual
            .Where(key => !baseline.Contains(key))
            .Select(static key => $"+ {key}: new public constructor with optional parameters - this needs a public API decision"));

        failures.AddRange(baseline
            .Where(key => !actual.Contains(key))
            .Order(StringComparer.Ordinal)
            .Select(static key => $"- {key}: stale entry - the constructor is gone, internal or has another signature; remove the line"));

        return failures;
    }

    internal static List<string> ActiveApiLines(IEnumerable<string> shipped, IEnumerable<string> unshipped)
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>();

        foreach (var line in unshipped)
        {
            if (line.StartsWith(RemovedPrefix, StringComparison.Ordinal))
            {
                removed.Add(line[RemovedPrefix.Length..].Trim());
            }
            else
            {
                lines.Add(line);
            }
        }

        lines.AddRange(shipped);

        return lines.Where(line => !removed.Contains(line.Trim())).ToList();
    }

    private static SortedSet<string> MeasureOptionalParameterConstructors()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var unshippedFile in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"), "PublicAPI.Unshipped.txt", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(unshippedFile)!;
            var package = Path.GetFileName(directory);
            var shippedFile = Path.Combine(directory, "PublicAPI.Shipped.txt");
            var shipped = File.Exists(shippedFile) ? File.ReadLines(shippedFile) : [];

            foreach (var line in ActiveApiLines(shipped, File.ReadLines(unshippedFile)))
            {
                if (ParseConstructor(line) is { Optional: > 0 } constructor)
                {
                    keys.Add($"{package}:{constructor.TypeName}({constructor.Parameters}/{constructor.Optional})");
                }
            }
        }

        return keys;
    }

    private static void RemoveStaleOptionalConstructorEntries(SortedSet<string> actual)
    {
        var builder = new StringBuilder();

        foreach (var line in File.ReadLines(OptionalConstructorBaselinePath))
        {
            var separator = line.IndexOf(" | ", StringComparison.Ordinal);

            if (line.Length > 0 && line[0] != '#' && separator > 0 && !actual.Contains(line[..separator].Trim()))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        File.WriteAllText(OptionalConstructorBaselinePath, builder.ToString());
    }

    private static List<string> SplitParameters(string parameters)
    {
        var result = new List<string>();

        if (parameters.Trim().Length == 0)
        {
            return result;
        }

        var depth = 0;
        var inString = false;
        var current = new StringBuilder();

        for (var index = 0; index < parameters.Length; index++)
        {
            var character = parameters[index];

            if (inString)
            {
                if (character == '\\' && index + 1 < parameters.Length)
                {
                    current.Append(character).Append(parameters[++index]);
                    continue;
                }

                inString = character != '"';
            }
            else if (character == '"')
            {
                inString = true;
            }
            else if (character is '<' or '(' or '[')
            {
                depth++;
            }
            else if (character is '>' or ')' or ']')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        result.Add(current.ToString());

        return result;
    }

    // A parameter is optional when " = " appears outside a string literal.
    private static bool IsOptionalParameter(string parameter)
    {
        var inString = false;

        for (var index = 0; index < parameter.Length; index++)
        {
            var character = parameter[index];

            if (inString && character == '\\')
            {
                index++;
            }
            else if (character == '"')
            {
                inString = !inString;
            }
            else if (!inString && string.CompareOrdinal(parameter, index, " = ", 0, 3) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string OptionalConstructorBaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "optional-parameter-constructor-baseline.txt");
}
