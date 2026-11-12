using System.Reflection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every configuration section a package binds is covered by a binding test of its own.
/// </summary>
/// <remarks>
/// <para>
/// The core test project walks the sections of Core and Abstractions and cannot see these: it does not reference the
/// provider, MCP, voice, workflow or SQL packages, because doing so would pull their SDKs into it. This project does, and
/// the rule it applies is the one the core project applies (<see cref="OptionsSectionCoverage"/>, linked): a section is
/// covered by a <c>{Type}BindingTests</c> class that has a test that can run and calls
/// <see cref="OptionsKeyCoverage.ShouldCoverEveryMember"/>.
/// </para>
/// <para>
/// The sections are found by scanning the package assemblies, not by reading the list of the permutation probe, so a
/// section a package adds tomorrow is found without anyone remembering to list it. A static class that only carries a
/// section name (the OpenAI-compatible entries bind <c>OpenAIProviderOptions</c>) has no members to cover and is skipped.
/// </para>
/// </remarks>
public sealed class PackageOptionsSectionCoverageTests
{
    [Fact]
    public void No_package_configuration_section_loses_its_binding_test()
    {
        var sections = SectionTypes().ToList();

        // A scan that found nothing, or lost a package, would pass for the wrong reason.
        sections.Count.ShouldBeGreaterThanOrEqualTo(11);

        var failures = OptionsSectionCoverage.BaselineDifferences(
            sections,
            typeof(PackageOptionsSectionCoverageTests).Assembly.GetTypes(),
            OptionsSectionCoverage.ReadBaseline(BaselinePath),
            "in this project");

        failures.ShouldBeEmpty(
            customMessage: $"The package options-section coverage baseline is stale.{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    private static string BaselinePath { get; } = Path.Combine(
        OptionsSectionCoverage.FindRepositoryRoot(), "tests", "Tracon.PackageOptions.UnitTests",
        "options-section-coverage-baseline.txt");

    /// <summary>One type from each package the project references; the scan reads the whole assembly of each.</summary>
    private static readonly Type[] PackageAssemblyAnchors =
    [
        typeof(AnthropicProviderOptions),
        typeof(AzureOpenAIProviderOptions),
        typeof(GoogleProviderOptions),
        typeof(OpenAIProviderOptions),
        typeof(TraconMcpOptions),
        typeof(VoiceOptions),
        typeof(TraconWorkflowOptions),
        typeof(TraconPostgreSqlOptions),
        typeof(TraconSqlServerOptions),
        typeof(TraconSqliteOptions),
    ];

    /// <summary>Every options class of the packages that names a <c>Tracon:</c> section and has members to bind.</summary>
    private static IEnumerable<Type> SectionTypes()
        => PackageAssemblyAnchors
            .Select(static anchor => anchor.Assembly)
            .Distinct()
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsPublic: true, IsAbstract: false })
            .Where(static type => type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)
                is { IsLiteral: true } field
                && field.GetRawConstantValue() is string name
                && name.StartsWith("Tracon", StringComparison.Ordinal))
            .Distinct();
}
