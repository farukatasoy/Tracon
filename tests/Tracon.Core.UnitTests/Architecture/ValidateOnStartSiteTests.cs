using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// An options type that has a validator in <c>src/</c> is validated when the host starts, not on first use.
/// </summary>
/// <remarks>
/// <para>
/// A validator registered without <c>ValidateOnStart</c> runs when something first reads the options. A host that
/// misconfigures such a section starts, serves, and fails on the first request that touches it, while the documentation of
/// the section says "at start-up". Two validators (the voice options and the live-voice options) were found like that.
/// </para>
/// <para>
/// The gate reads the source, not the container: it collects every options type named by an <c>IValidateOptions&lt;T&gt;</c>
/// implementation and every type registered through one of the helpers that start-validate
/// (<c>UnreadableConfigurationValidator.Add&lt;T&gt;</c>, <c>AddValidatedOptions&lt;T, …&gt;</c>) or through
/// <c>AddOptions&lt;T&gt;(…)</c> followed by <c>.ValidateOnStart()</c>, and requires the first set to be inside the second. The
/// match is by type, across the whole tree: the registration of an options type and its validator live in different files.
/// </para>
/// </remarks>
public sealed partial class ValidateOnStartSiteTests
{
    [Fact]
    public void Every_options_type_with_a_validator_is_validated_on_start()
    {
        var sources = ReadSources();

        var validated = sources
            .SelectMany(static text => ValidatorImplementation().Matches(text).Select(static match => match.Groups["type"].Value))
            .Select(SimpleName)
            .Where(static name => !IsGenericParameter(name))
            .ToHashSet(StringComparer.Ordinal);

        var started = sources
            .SelectMany(static text => StartValidation(text))
            .Select(SimpleName)
            .ToHashSet(StringComparer.Ordinal);

        validated.Count.ShouldBeGreaterThan(30, "the scan found the validators of the options types; an empty set would pass every type");

        validated
            .Where(name => !started.Contains(name))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "these options types have an IValidateOptions<T> but nothing validates them on start: register them through " +
                "UnreadableConfigurationValidator.Add<T>, ProviderRegistrationCore.AddValidatedOptions, or AddOptions<T>().ValidateOnStart()");
    }

    [Fact]
    public void The_scan_sees_the_three_ways_a_type_is_start_validated()
    {
        const string Text = """
            UnreadableConfigurationValidator.Add<FirstOptions>(services, "Tracon:First");
            ProviderRegistrationCore.AddValidatedOptions<SecondOptions, SecondValidator>(services, name);
            services
                .AddOptions<ThirdOptions>("named")
                .Configure(options => { })
                .ValidateOnStart();
            services.AddOptions<FourthOptions>().Configure(options => { });
            """;

        StartValidation(Text).ShouldBe(["FirstOptions", "SecondOptions", "ThirdOptions"], ignoreOrder: true);
    }

    [Fact]
    public void The_scan_ignores_a_validate_on_start_that_belongs_to_the_next_options_type()
    {
        const string Text = """
            services.AddOptions<AloneOptions>().Configure(options => { });
            services.AddOptions<OtherOptions>().ValidateOnStart();
            """;

        StartValidation(Text).ShouldBe(["OtherOptions"]);
    }

    private static IEnumerable<string> StartValidation(string text)
    {
        foreach (Match match in HelperRegistration().Matches(text))
        {
            yield return match.Groups["type"].Value;
        }

        // AddOptions<T>(…) … .ValidateOnStart() inside one statement: no semicolon between them.
        foreach (Match match in ChainedValidateOnStart().Matches(text))
        {
            yield return match.Groups["type"].Value;
        }
    }

    private static string SimpleName(string type)
    {
        var generic = type.IndexOf('<', StringComparison.Ordinal);
        var name = generic < 0 ? type : type[..generic];

        return name[(name.LastIndexOf('.') + 1)..];
    }

    // TOptions, TSettings: the type parameter of a generic validator, not an options type.
    private static bool IsGenericParameter(string name) => name.Length > 1 && name[0] == 'T' && char.IsUpper(name[1]) && !name.EndsWith("Options", StringComparison.Ordinal) || name is "TOptions";

    private static List<string> ReadSources()
    {
        var root = FindRepositoryRoot();
        var separator = Path.DirectorySeparatorChar;

        return [.. Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Select(File.ReadAllText)];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }

    // class X : …, IValidateOptions<T>   (also a primary-constructor class and a record)
    [GeneratedRegex(@"(?:class|record)\s+\w+(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*[^{;]*?IValidateOptions<(?<type>[\w.]+)>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ValidatorImplementation();

    [GeneratedRegex(@"(?:UnreadableConfigurationValidator\.Add|AddValidatedOptions)<(?<type>[\w.]+)\s*[,>]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex HelperRegistration();

    [GeneratedRegex(@"AddOptions<(?<type>[\w.]+)>[^;]*?\.ValidateOnStart\(\)", RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ChainedValidateOnStart();
}
