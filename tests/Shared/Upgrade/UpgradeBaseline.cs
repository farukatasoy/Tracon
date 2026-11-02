using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tracon.UpgradeGate;

/// <summary>One migration file of the last release.</summary>
/// <param name="Id">The sequence number.</param>
/// <param name="Name">The name, for example <c>0001_initial</c>.</param>
/// <param name="Checksum">The checksum the release wrote into the ledger of every database it migrated.</param>
/// <param name="Statements">The digest of the statements with the comments and the spacing removed.</param>
internal sealed record BaselineMigration(int Id, string Name, string Checksum, string Statements);

/// <summary>A migration file whose statements differ from the release on purpose.</summary>
/// <param name="Provider">The provider directory name.</param>
/// <param name="Name">The migration name.</param>
/// <param name="Statements">The digest of the statements as they stand now.</param>
/// <param name="Reason">Why the file was edited.</param>
internal sealed record BaselineExemption(string Provider, string Name, string Statements, string Reason);

/// <summary>
/// The last release, as far as the upgrade gate needs to know it: what every migration file of it said.
/// </summary>
/// <remarks>
/// The manifest is written by <c>scripts/upgrade_baseline.py</c> from the release tag and moves forward with every
/// release. It holds digests, not the files: the gate builds the schema of the release from the migrations in the
/// tree and proves with the digests that they are the migrations the release shipped.
/// </remarks>
internal sealed class UpgradeBaseline
{
    private const string ResourceName = "UpgradeBaseline.json";

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex LineComment = new(@"--[^\n]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex Whitespace = new(@"[ \t\r\n\f\v]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private UpgradeBaseline(
        string version,
        string tag,
        IReadOnlyDictionary<string, IReadOnlyList<BaselineMigration>> providers,
        IReadOnlyList<BaselineExemption> exemptions)
    {
        Version = version;
        Tag = tag;
        Providers = providers;
        Exemptions = exemptions;
    }

    /// <summary>Gets the version of the release.</summary>
    public string Version { get; }

    /// <summary>Gets the git tag of the release.</summary>
    public string Tag { get; }

    /// <summary>Gets the migration files of the release by provider directory name (<c>sqlite</c>, <c>postgresql</c>, <c>sqlserver</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BaselineMigration>> Providers { get; }

    /// <summary>Gets the files edited on purpose since the release.</summary>
    public IReadOnlyList<BaselineExemption> Exemptions { get; }

    /// <summary>
    /// Gets whether a database of this release may refuse to start under today's files because only a comment moved.
    /// </summary>
    /// <remarks>
    /// A preview line has no upgrade path for stored data between previews; from the first release candidate on an
    /// applied migration is never edited, so a database of a candidate or a final release is checked byte for byte.
    /// </remarks>
    public bool ChecksumsMayDrift => Version.Contains("-preview", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the manifest embedded in the test assembly.</summary>
    /// <returns>The manifest.</returns>
    public static UpgradeBaseline Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The test assembly does not embed '{ResourceName}'.");

        var root = JsonNode.Parse(stream)!.AsObject();

        var providers = root["providers"]!.AsObject().ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<BaselineMigration>)[.. pair.Value!.AsArray().Select(static entry => new BaselineMigration(
                entry!["id"]!.GetValue<int>(),
                entry["name"]!.GetValue<string>(),
                entry["checksum"]!.GetValue<string>(),
                entry["statements"]!.GetValue<string>()))],
            StringComparer.Ordinal);

        var exemptions = root["exemptions"]!.AsArray().Select(static entry => new BaselineExemption(
            entry!["provider"]!.GetValue<string>(),
            entry["name"]!.GetValue<string>(),
            entry["statements"]!.GetValue<string>(),
            entry["reason"]!.GetValue<string>())).ToList();

        return new UpgradeBaseline(root["version"]!.GetValue<string>(), root["tag"]!.GetValue<string>(), providers, exemptions);
    }

    /// <summary>
    /// Digests the statements of a migration: the comments and the spacing do not count, a changed statement does.
    /// </summary>
    /// <param name="sql">The migration text.</param>
    /// <returns>The lowercase hexadecimal SHA-256.</returns>
    /// <remarks>The same algorithm as <c>statement_digest</c> in <c>scripts/upgrade_baseline.py</c>.</remarks>
    public static string StatementDigest(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var text = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = BlockComment.Replace(text, " ");
        text = LineComment.Replace(text, " ");
        text = Whitespace.Replace(text, " ").Trim();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
