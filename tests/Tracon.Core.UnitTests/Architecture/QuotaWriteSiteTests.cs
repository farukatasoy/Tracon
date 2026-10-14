namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// One place builds a <see cref="QuotaConsumption"/> for the quota counters:
/// <c>TreeQuota.RecordAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Four writers add to a tenant's counter - the agent root, a child agent run, the workflow
/// root and a tool call that outlives its run. Each one that builds its own entry is a place
/// the next thing a run spends gets added in three of them and forgotten in the fourth: the
/// cache charge went missing from a tenant's ceiling that way (K-483), and the tenant of a
/// background child would have been whatever the task happened to carry (F-418).
/// </para>
/// <para>
/// The gate does not read the code; it makes a NEW writer fail until it goes through the one
/// path. A test that builds an entry to prove a store or the enforcer is not a writer, and
/// this scan reads <c>src/</c> only.
/// </para>
/// </remarks>
public sealed class QuotaWriteSiteTests
{
    private const string TheOnePath = "src/Tracon.Core/Quotas/TreeQuota.cs";

    [Fact]
    public void Only_the_tree_quota_path_builds_a_quota_entry_in_the_product()
    {
        var root = FindRepositoryRoot();

        var sites = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadLines(path).Any(IsConstruction))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        sites.ShouldBe(
            [TheOnePath],
            "A quota entry is built in one place, TreeQuota.RecordAsync, which names the tree's tenant and root. " +
            "Add a writer there instead of building a QuotaConsumption of your own.");
    }

    /// <summary>
    /// Every place that opens a run scope with a budget of its own names the counter the
    /// tree's spend goes to. A root that did not would leave its children (and a tool that
    /// outlives its run) with no account, and their spend in no counter.
    /// </summary>
    private static readonly string[] RootScopeSites =
    [
        "src/Tracon.Core/Recording/RunRecordingAgent.Lifecycle.cs",
        "src/Tracon.Workflows/Internal/WorkflowRunner.cs",
    ];

    [Fact]
    public void Every_place_that_opens_a_root_scope_names_the_account_its_tree_adds_to()
    {
        var root = FindRepositoryRoot();

        var sites = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadLines(path).Any(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                                                                  line.Contains("new AgentRunScope", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        sites.ShouldBe(
            [.. RootScopeSites],
            "A new place opens a run scope. Give its budget a TreeAccount (budget.Account = new TreeAccount(...)) " +
            "before the first child can start, and add the file here.");

        foreach (var site in RootScopeSites)
        {
            File.ReadAllText(Path.Combine(root, site))
                .Contains("Account = new TreeAccount(", StringComparison.Ordinal)
                .ShouldBeTrue($"{site} opens a root scope and must assign the tree's account");
        }
    }

    [Theory]
    [InlineData("        new QuotaConsumption", true)]
    [InlineData("var entry = new QuotaConsumption { TenantId = tenant };", true)]
    [InlineData("    /// <c>grep -rn \"new QuotaConsumption\" src/</c> shows this file and nothing else.", false)]
    [InlineData("        // new QuotaConsumption is built once", false)]
    [InlineData("var other = new QuotaConsumptionSummary();", false)]
    public void The_scan_sees_a_construction_and_skips_a_comment(string line, bool expected)
        => IsConstruction(line).ShouldBe(expected);

    private static bool IsConstruction(string line)
    {
        var trimmed = line.TrimStart();

        return !trimmed.StartsWith("//", StringComparison.Ordinal) &&
               System.Text.RegularExpressions.Regex.IsMatch(
                   line,
                   @"\bnew\s+QuotaConsumption\b(?!\w)",
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                   TimeSpan.FromSeconds(5));
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
}
