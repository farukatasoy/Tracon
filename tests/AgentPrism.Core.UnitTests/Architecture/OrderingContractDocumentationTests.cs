using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces that every public member carrying an ordering or precedence contract states
/// its DIRECTION, in wording a third party can act on without reading the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The defect class this guards (BL-034): a numeric ordering knob is easy to document
/// backwards, and no behavior test can see it. Three vakas were measured on 2026-08-27
/// and all three sat on the extension surface a third party reads.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>IAgentDecorator.Order</c> said the direction backwards - "a lower value wraps
/// inside" - while the pipeline sorts descending and applies the highest value FIRST,
/// making it innermost. The example sentence right after it said the opposite and was
/// the correct one, so the interface contradicted itself.
/// </description></item>
/// <item><description>
/// <c>IAgentSource.Priority</c> dropped the direction word entirely: "the source with
/// priority wins" states nothing.
/// </description></item>
/// <item><description>
/// <c>IAgentCatalog.ListAsync</c> said "the source with the higher priority wins", which
/// reads as the higher NUMBER next to <c>AgentSourcePriority.Database = 100</c>, while
/// the lower number is what actually wins.
/// </description></item>
/// </list>
/// <para>
/// <c>CompositeAgentCatalog</c>'s own internal remark stated the rule correctly the whole
/// time ("the higher-priority source with the lower number wins"), and the priority
/// BEHAVIOR was already covered by <c>CompositeAgentCatalogTests</c>. Neither helped: the
/// public sentence is the only contract a packaged consumer gets. That is why this gate
/// reads source text rather than adding another behavior test.
/// </para>
/// <para>
/// Phrases are matched literally. Rewording one fails this test on purpose - the
/// direction may only change when someone re-verifies it against the ordering code.
/// </para>
/// </remarks>
public sealed class OrderingContractDocumentationTests
{
    private static readonly Regex DocTag = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Whitespace = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    public static TheoryData<string, string, string[]> Contracts() => new()
    {
        {
            // OrderByDescending + wrap-the-previous => highest order is applied first, so it is innermost.
            Path.Combine("AgentPrism.Abstractions", "Agents", "IAgentDecorator.cs"),
            "int Order { get; }",
            ["lower value wraps outside", "higher value wraps inside"]
        },
        {
            // CompositeAgentCatalog orders sources ascending, and the first source to claim a name keeps it.
            Path.Combine("AgentPrism.Abstractions", "Agents", "IAgentSource.cs"),
            "int Priority { get; }",
            ["lower value is tried first", "lower value wins"]
        },
        {
            // Same rule, restated on the surface that returns the deduplicated list.
            // The phrase is matched WHOLE: an earlier draft asked for "lower" and
            // "value wins" separately, and a deliberate break (flipping the winner to
            // "higher") still passed, because a later clause in the same summary also
            // said "lower". A split phrase is not a gate.
            Path.Combine("AgentPrism.Abstractions", "Agents", "IAgentCatalog.cs"),
            "ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync",
            ["lower value wins"]
        },
        {
            // BL-008 (phase 122): the example used to register AFTER
            // AddAgentPrism(), contradicting the "registered before wins"
            // prose right above it - K-642's exact defect shape, on a
            // different contract. The phrase is bonded to the example's OWN
            // code text, not to the prose, because the prose was already
            // correct: only the example regressed, and a phrase describing
            // the rule in words would stay green even if the example's call
            // order flipped back.
            Path.Combine("AgentPrism.Core", "IAgentPrismBuilder.cs"),
            "IServiceCollection Services { get; }",
            ["addsingleton(new ordergateway()); builder.addagentprism();"]
        },
    };

    [Theory]
    [MemberData(nameof(Contracts))]
    public void An_ordering_contract_states_which_direction_wins(
        string relativePath,
        string memberDeclaration,
        string[] requiredPhrases)
    {
        var summary = SummaryAbove(relativePath, memberDeclaration);

        foreach (var phrase in requiredPhrases)
        {
            summary.ShouldContain(
                phrase,
                Case.Insensitive,
                $"'{relativePath}' documents an ordering contract, so its summary has to say "
                + "which direction wins in words a consumer can act on. Verify the phrase "
                + "against the ordering code before changing it here.");
        }
    }

    private static string SummaryAbove(string relativePath, string memberDeclaration)
    {
        var path = Path.Combine(RepositoryRoot(), "src", relativePath);
        var source = File.ReadAllText(path);

        var member = source.IndexOf(memberDeclaration, StringComparison.Ordinal);

        member.ShouldBeGreaterThan(
            -1,
            $"'{path}' no longer declares '{memberDeclaration}'; this gate has gone stale.");

        var block = source[..member];
        var start = block.LastIndexOf("/// <summary>", StringComparison.Ordinal);

        start.ShouldBeGreaterThan(
            -1,
            $"'{memberDeclaration}' in '{path}' lost its <summary> documentation.");

        var text = DocTag.Replace(block[start..], string.Empty)
            .Replace("///", " ", StringComparison.Ordinal);

        return Whitespace.Replace(text, " ").Trim();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}'.");
    }
}
