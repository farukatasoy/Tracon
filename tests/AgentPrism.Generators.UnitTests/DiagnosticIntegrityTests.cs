using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Keeps every diagnostic honest. A diagnostic teaches an API and points at a
/// section of the capability map; when either is renamed and the message is not,
/// the diagnostic keeps teaching something that no longer exists, which is worse
/// than reporting nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// The convention this rests on: <strong>an API named in a diagnostic is written
/// in single quotes</strong>. Every quoted identifier must be part of the tracked
/// public API, or be listed in <see cref="ExternalApis"/> - the types the
/// messages legitimately borrow from MAF and the BCL.
/// </para>
/// <para>
/// The second assertion joins the two layers: a help link resolves to a real
/// heading of <c>capabilities.md</c>, so deleting a section from the capability
/// map turns this test red.
/// </para>
/// </remarks>
public sealed class DiagnosticIntegrityTests
{
    /// <summary>
    /// Names that a message may use although the tracked public API does not
    /// declare them: MAF types, and the registration the source generator emits
    /// into the consumer's own compilation.
    /// </summary>
    private static readonly HashSet<string> ExternalApis = new(StringComparer.Ordinal)
    {
        "AIAgent",
        "AIFunctionFactory.Create",
        "AddGeneratedTools",
    };

    private static readonly Regex QuotedPattern = new(
        @"'(?<token>[^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A file name inside a quoted value - <c>AGENTS.md</c>. Removed before the
    /// API names are read, so the file name is not mistaken for a type.
    /// </summary>
    private static readonly Regex FileNamePattern = new(
        @"\b[A-Za-z0-9_]+\.(?:md|txt|json|cs|targets|props|editorconfig)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// An API name inside a quoted value. Matching happens INSIDE the quotes
    /// rather than against the whole value, because the dominant form is a call
    /// - <c>'MapAgentPrism()'</c> - and a nested one exists as well:
    /// <c>'AddTool(AIFunctionFactory.Create(...))'</c> names two APIs. An
    /// earlier version compared the whole quoted value against this shape and
    /// silently checked nothing in eleven of thirteen diagnostics.
    /// </summary>
    private static readonly Regex ApiNamePattern = new(
        @"\b[A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex HeadingPattern = new(
        @"^## (?<title>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ApgCodePattern = new(
        @"APG\d{4}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    public static TheoryData<string> DiagnosticIds() => [.. Descriptors().Select(descriptor => descriptor.Id)];

    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public void Every_api_a_diagnostic_teaches_still_exists(string id)
    {
        var descriptor = Descriptors().Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        var publicApi = ReadPublicApi();
        var missing = new List<string>();

        foreach (var text in new[] { descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture), descriptor.Description.ToString(CultureInfo.InvariantCulture) })
        {
            foreach (Match quoted in QuotedPattern.Matches(text))
            {
                var value = FileNamePattern.Replace(quoted.Groups["token"].Value, string.Empty);

                foreach (Match name in ApiNamePattern.Matches(value))
                {
                    if (ExternalApis.Contains(name.Value))
                    {
                        continue;
                    }

                    if (!publicApi.Contains(name.Value, StringComparison.Ordinal))
                    {
                        missing.Add(name.Value);
                    }
                }
            }
        }

        missing.ShouldBeEmpty(
            $"{id} names an API that no PublicAPI.*.txt declares: {string.Join(", ", missing)}. " +
            "Rename it in the message, or add it to ExternalApis when AgentPrism does not own it.");
    }

    [Fact]
    public void Every_provider_registration_APG0102_recommends_still_exists()
    {
        var publicApi = ReadPublicApi();

        foreach (var provider in AgentPrismUsageAnalyzer.BuiltInProviders)
        {
            var registration = provider.Value.Replace("()", string.Empty, StringComparison.Ordinal);

            publicApi.ShouldContain(
                registration,
                Case.Sensitive,
                $"APG0102 tells the consumer to call '{provider.Value}' for provider '{provider.Key}', " +
                "but no package declares it.");
        }
    }

    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public void Every_help_link_resolves_to_a_section_of_the_capability_map(string id)
    {
        var descriptor = Descriptors().Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

        descriptor.HelpLinkUri.ShouldNotBeNullOrEmpty($"{id} carries no help link.");

        var anchor = descriptor.HelpLinkUri.Split('#').Last();
        var page = File.ReadAllText(CapabilityMapPath);

        var anchors = HeadingPattern.Matches(page)
            .Select(match => Slug(match.Groups["title"].Value))
            .ToList();

        anchors.ShouldContain(
            candidate => string.Equals(candidate, anchor, StringComparison.Ordinal),
            $"{id} points at '#{anchor}', which is not a heading of capabilities.md. Known: {string.Join(", ", anchors)}");

        descriptor.HelpLinkUri.ShouldStartWith(
            $"{SiteUrl}capabilities/#",
            Case.Sensitive,
            $"{id} points at an address the site does not publish; site.config.mjs declares {SiteUrl}.");
    }

    /// <summary>
    /// A coding agent's first sight of an <c>APG</c> code is the build log, and
    /// the only page that explains all of them is <c>troubleshooting.md</c> - it
    /// already carries nine of the fourteen (F-136). A code missing from it has
    /// no explanation anywhere a consumer would look.
    /// </summary>
    [Theory]
    [MemberData(nameof(DiagnosticIds))]
    public void Every_diagnostic_is_explained_on_the_troubleshooting_page(string id)
    {
        var page = File.ReadAllText(TroubleshootingPath);

        page.ShouldContain(id, Case.Sensitive, $"{id} does not appear on troubleshooting.md.");
    }

    /// <summary>
    /// The reverse direction: a code the page still names after its descriptor
    /// was removed is a dead reference - it sends a consumer looking for a
    /// diagnostic that can never fire.
    /// </summary>
    [Fact]
    public void The_troubleshooting_page_names_no_diagnostic_that_no_longer_exists()
    {
        var page = File.ReadAllText(TroubleshootingPath);
        var known = Descriptors().Select(descriptor => descriptor.Id).ToHashSet(StringComparer.Ordinal);

        var stale = ApgCodePattern.Matches(page)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Where(code => !known.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToList();

        stale.ShouldBeEmpty($"troubleshooting.md names a diagnostic no descriptor declares: {string.Join(", ", stale)}.");
    }

    /// <summary>Starlight and GitHub heading slug.</summary>
    private static string Slug(string title)
    {
        var slug = new string([.. title.ToLowerInvariant().Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-')]);

        return slug.Trim().Replace(' ', '-');
    }

    private static IReadOnlyList<DiagnosticDescriptor> Descriptors()
        => [.. new[] { typeof(ToolDiagnostics), typeof(UsageDiagnostics) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)];

    private static string ReadPublicApi()
    {
        var files = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"),
            "PublicAPI.*.txt",
            SearchOption.AllDirectories);

        return string.Join("\n", files.Select(File.ReadAllText));
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string CapabilityMapPath { get; } =
        Path.Combine(RepositoryRoot, "docs-site", "src", "content", "docs", "capabilities.md");

    private static string TroubleshootingPath { get; } =
        Path.Combine(RepositoryRoot, "docs-site", "src", "content", "docs", "troubleshooting.md");

    /// <summary>Where the documentation site is published, ending in a slash.</summary>
    /// <remarks>
    /// The address is declared twice - as a C# constant in <c>DocumentationLinks</c>,
    /// which ships inside the analyzer package, and as <c>site</c> in
    /// <c>docs-site/site.config.mjs</c>, which the Astro build imports. Neither language
    /// can read the other's declaration, so this gate reads the JavaScript one and turns
    /// red when the shipped help links stop agreeing with it. Same arrangement as
    /// <c>ShippedDocumentationSelfContainmentTests</c> and the internal-history pattern.
    /// </remarks>
    private static string SiteUrl { get; } = ReadSiteUrl();

    private static string ReadSiteUrl()
    {
        var path = Path.Combine(RepositoryRoot, "docs-site", "site.config.mjs");
        var declaration = File.ReadAllText(path);

        var site = Match(declaration, "site");
        var basePath = Match(declaration, "base");

        if (site.Length == 0 || basePath.Length == 0)
        {
            throw new InvalidOperationException($"{path} no longer declares both 'site' and 'base'.");
        }

        return site + basePath;

        static string Match(string declaration, string name) => Regex.Match(
            declaration,
            $"export const {name} = '(?<value>[^']+)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)).Groups["value"].Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
