using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tracon.Core.UnitTests.Architecture;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// A scalar setting that starts with a value says that value in its own documentation.
/// </summary>
/// <remarks>
/// <para>
/// The configuration reference of the site names the defaults, and a consumer who works from the XML documentation
/// on disk (the local reference, an IDE tooltip, a coding agent) has only the property's own text. A setting whose
/// text names no default leaves that reader to run the code to learn what happens when the section is absent: a
/// process that connects to a shared SQL database without a <c>Tracon:Scheduling</c> section joined the job queue, and
/// nothing said so (measured: 262 of the 436 scalar settings of the options classes were silent).
/// </para>
/// <para>
/// A ratchet, in the shape of <see cref="CapabilityExampleTests"/>. <c>option-defaults-baseline.txt</c> lists the
/// settings allowed to stay silent. It was written from the state of 2026-11-24 and may only shrink: a setting added
/// without its default fails, and a setting that states its default and is still on the list fails too.
/// </para>
/// <para>
/// <strong>What counts as a default stated.</strong> The word <c>default</c> in the text of the summary or the
/// remarks, or a number with a unit (<c>10 seconds</c>, <c>5 minutes</c>, <c>8,192 characters</c>). A cross reference
/// to a member that has <c>Default</c> in its name is no text and does not count: only what the reader sees does.
/// A setting that starts at the zero value of its type (<see langword="false"/>, <c>0</c>, <see langword="null"/>) has
/// no starting value to state and is out of scope. This reads the scalar settings of <c>Tracon.Core</c> and
/// <c>Tracon.Abstractions</c>; the options of the provider, MCP, voice, workflow and SQL packages are not read here.
/// </para>
/// <para>
/// Refresh after a reviewed change:
/// <c>TRACON_OPTION_DEFAULTS_REFRESH=1 dotnet test --project tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class OptionDefaultsDocumentedTests
{
    private const string RefreshEnvVar = "TRACON_OPTION_DEFAULTS_REFRESH";

    private const string ReadTargetFramework = "net10.0";

    private static readonly Regex StatesADefault = new(
        @"\bdefault(?:s|ed)?\b|\b\d[\d,.]*\s*(?:ms|milliseconds?|seconds?|minutes?|hours?|days?|bytes?|KiB|MiB|characters?|items?|rows?|tokens?)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Every_scalar_option_that_starts_with_a_value_says_it_or_is_in_the_shrinking_baseline()
    {
        var settings = ScalarSettingsWithAStartingValue();

        // A scan that read nothing would pass for the wrong reason.
        settings.Count.ShouldBeGreaterThan(50, "the options classes of Core and Abstractions were not found");

        var documentation = ReadDocumentation();
        var unreadable = settings.Where(setting => !documentation.ContainsKey(setting.Id)).Select(static setting => setting.Name).ToList();

        // Without the XML nothing is documented and the baseline would grow without anyone noticing.
        unreadable.ShouldBeEmpty(
            "No XML documentation was found for these settings; build the solution first " +
            $"(dotnet build Tracon.slnx -c Release): {string.Join(", ", unreadable.Take(10))}");

        var silent = settings
            .Where(setting => !StatesADefault.IsMatch(documentation[setting.Id]))
            .Select(static setting => setting.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(silent);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        failures.AddRange(silent.Where(name => !baseline.Contains(name))
            .Select(static name => $"+ {name}: starts with a value and its documentation names no default"));
        failures.AddRange(baseline.Where(name => !silent.Contains(name, StringComparer.Ordinal))
            .Select(static name => $"- {name}: states its default now (or is gone), but is still in the baseline"));

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: "The documented defaults and the baseline disagree.\n" +
                           $"{string.Join('\n', failures)}\n" +
                           "Say the default in the property's summary (\"Defaults to 10 seconds.\"), or, for a reviewed " +
                           $"removal, refresh the baseline: {RefreshEnvVar}=1 dotnet test --project tests/Tracon.Core.UnitTests -c Release");
    }

    /// <summary>
    /// The first two options classes of the scheduling and approval packages were the ones whose silence cost a
    /// consumer a surprise; they carry every default and must never come back to the list.
    /// </summary>
    [Fact]
    public void The_scheduling_and_approval_options_state_every_default()
    {
        var baseline = ReadBaseline();

        baseline.Where(static name => name.StartsWith("TraconSchedulingOptions.", StringComparison.Ordinal)
                || name.StartsWith("TraconApprovalOptions.", StringComparison.Ordinal))
            .ShouldBeEmpty();

        var named = ScalarSettingsWithAStartingValue()
            .Where(static setting => setting.Name.StartsWith("TraconSchedulingOptions.", StringComparison.Ordinal)
                || setting.Name.StartsWith("TraconApprovalOptions.", StringComparison.Ordinal))
            .Select(static setting => setting.Name)
            .ToList();

        named.ShouldContain(static name => string.Equals(name, "TraconSchedulingOptions.RunWorker", StringComparison.Ordinal));
        named.ShouldContain(static name => string.Equals(name, "TraconSchedulingOptions.PollInterval", StringComparison.Ordinal));
        named.ShouldContain(static name => string.Equals(name, "TraconApprovalOptions.ScanInterval", StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves the pattern: a sentence that names a default passes, and prose that only points at a member whose
    /// name carries the word does not.
    /// </summary>
    [Theory]
    [InlineData("Whether the worker runs. Defaults to <true/>.", true)]
    [InlineData("How often to look. 10 seconds.", true)]
    [InlineData("The default is 24 hours.", true)]
    [InlineData("The limit, 8,192 characters.", true)]
    [InlineData("The lane the job is queued under, as <see cref=\"JobLanes.Default\"/> says.", false)]
    [InlineData("Whether the subsystem is enabled.", false)]
    public void The_pattern_reads_a_stated_default_and_nothing_else(string summary, bool expected)
    {
        // A <see/> element holds no text, which is what an XML reader sees; the sample strips it the same way.
        var text = Regex.Replace(summary, "<[^>]*/>", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        StatesADefault.IsMatch(text).ShouldBe(expected);
    }

    private static List<Setting> ScalarSettingsWithAStartingValue()
    {
        var found = new List<Setting>();
        var assemblies = new[] { typeof(TraconOptions).Assembly, typeof(TraconSchedulingOptions).Assembly }.Distinct();

        foreach (var type in assemblies.SelectMany(static assembly => assembly.GetTypes())
                     .Where(static type => type is { IsClass: true, IsPublic: true, IsAbstract: false }
                         && type.Name.EndsWith("Options", StringComparison.Ordinal)
                         && type.GetConstructor(Type.EmptyTypes) is not null)
                     .OrderBy(static type => type.Name, StringComparer.Ordinal))
        {
            object? instance;

            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(static property => property.GetMethod is not null && property.SetMethod is { IsPublic: true }
                             && property.GetIndexParameters().Length == 0
                             && IsScalar(property.PropertyType))
                         .OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                if (HasStartingValue(property.GetValue(instance), property.PropertyType))
                {
                    found.Add(new Setting($"{type.Name}.{property.Name}", $"P:{type.FullName}.{property.Name}"));
                }
            }
        }

        return found;
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(bool)
            || underlying == typeof(string)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(int)
            || underlying == typeof(long)
            || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying.IsEnum;
    }

    private static bool HasStartingValue(object? value, Type type)
    {
        if (value is null)
        {
            return false;
        }

        if (value is string text)
        {
            return text.Length > 0;
        }

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return !value.Equals(Activator.CreateInstance(underlying));
    }

    /// <summary>Reads the summary and the remarks of every property the built XML documents.</summary>
    private static Dictionary<string, string> ReadDocumentation()
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var binaries = Path.Combine(CapabilityEntryPoints.RepositoryRoot, "artifacts", "bin");

        foreach (var package in new[] { "Tracon.Abstractions", "Tracon.Core" })
        {
            var file = Path.Combine(binaries, package, $"{Configuration}_{ReadTargetFramework}", $"{package}.xml");

            if (!File.Exists(file))
            {
                continue;
            }

            foreach (var member in XDocument.Load(file).Descendants("member"))
            {
                var id = member.Attribute("name")?.Value;

                if (id is null || !id.StartsWith("P:", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = new StringBuilder();

                foreach (var element in member.Elements().Where(static element => element.Name.LocalName is "summary" or "remarks"))
                {
                    text.Append(element.Value).Append(' ');
                }

                texts[id] = text.ToString();
            }
        }

        return texts;
    }

    private static string Configuration { get; } = new DirectoryInfo(AppContext.BaseDirectory).Name.Split('_')[0];

    private static string BaselinePath { get; } = Path.Combine(
        CapabilityEntryPoints.RepositoryRoot, "tests", "Tracon.Core.UnitTests", "Configuration", "option-defaults-baseline.txt");

    private static SortedSet<string> ReadBaseline()
    {
        var baseline = new SortedSet<string>(StringComparer.Ordinal);

        if (!File.Exists(BaselinePath))
        {
            return baseline;
        }

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                baseline.Add(line.Trim());
            }
        }

        return baseline;
    }

    private static void WriteBaseline(IEnumerable<string> silent)
    {
        var builder = new StringBuilder();

        builder.Append("# Generated by OptionDefaultsDocumentedTests. Refresh:\n");
        builder.Append($"#   {RefreshEnvVar}=1 dotnet test --project tests/Tracon.Core.UnitTests -c Release\n");
        builder.Append("# One <OptionsType>.<Property> per line: a scalar setting that starts with a value and whose\n");
        builder.Append("# documentation names no default. The list may only SHRINK; say the default in the property's summary.\n");

        foreach (var name in silent.Order(StringComparer.Ordinal))
        {
            builder.Append(name).Append('\n');
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private sealed record Setting(string Name, string Id);
}
