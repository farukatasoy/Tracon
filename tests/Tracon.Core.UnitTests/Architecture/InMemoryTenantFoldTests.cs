using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every in-memory store folds the tenant identifier the way the SQL stores do.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The rule: a tenant identifier is matched case-insensitively by normalizing the <em>value</em>
/// (<see cref="AmbientTenantScope.Normalize"/>), on every write and every query. A store that compares the raw
/// text splits one tenant in two. The SQL stores are held to this by a scan of their command parameters; this is
/// the same scan for the in-memory family.
/// </para>
/// <para>
/// The contract tests cannot close the gap alone. They reach a store through four hooks, and a store has many
/// more methods than that: a method no hook calls can forget the fold and every contract test stays green. This
/// is the same class of defect as a hand-repeated sum that gains a term.
/// </para>
/// <para>
/// Three shapes carry a tenant into a store, and each has one structural answer:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// The store reads the tenant from an <see cref="ITenantContext"/>. The field is assigned the wrapped context
/// (<c>CanonicalTenantContext.Wrap</c>), so every read is canonical and a method added later cannot skip it.
/// </description>
/// </item>
/// <item>
/// <description>
/// A public method takes the tenant as a <c>tenantId</c> parameter. Its body folds the parameter. A private
/// helper receives the canonical value and is not scanned.
/// </description>
/// </item>
/// <item>
/// <description>
/// A query or request object carries the tenant as a field (<c>query.TenantId</c>, <c>info.TenantId</c>,
/// <c>request.TenantId</c>, <c>draft.TenantId</c>, <c>batch.TenantId</c>, <c>content.TenantId</c>). Every read of
/// such a field folds it on the same line. A hook that sends no tenant (a <see langword="null"/> query tenant
/// means the ambient one) never reaches these lines, which is why the contract tests cannot guard them.
/// </description>
/// </item>
/// </list>
/// <para>
/// A tenant that arrives inside a record a store keeps (<c>record.TenantId</c>, <c>policy.TenantId</c>) has no
/// textual shape this scan can follow, because the same name is read before and after the fold. The base
/// contract writes every such record through its seed hook under a spelling the rule changes, and the session
/// and run contracts read the record back; a reviewer reads the rest.
/// </para>
/// </remarks>
public sealed class InMemoryTenantFoldTests
{
    private const string FoldedWrapper = "CanonicalTenantContext.Wrap(";

    private static readonly string[] AcceptedFolds =
    [
        "AmbientTenantScope.Normalize(tenantId)",
        "AmbientTenantScope.NormalizeOrNull(tenantId)",
        // The retention policy store folds a tenant and also keeps the "*" default apart from one.
        "RetentionPolicyTenant.Normalize(tenantId)",
    ];

    /// <summary>
    /// The objects that carry a tenant into a store to be read, not kept: a query, a start or request object, a
    /// draft, a batch of spans, an upload.
    /// </summary>
    private static readonly Regex CarriedTenantRead = new(
        @"\b(?<owner>query|info|request|draft|batch|content)\.TenantId\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Public methods that take a <c>tenantId</c> and do not fold it, as <c>File.cs:Method</c>, each with the
    /// reason. An entry that names a method that does not exist fails the scan, so the list cannot go stale.
    /// </summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["InMemoryWorkflowStores.cs:Append"] =
            "Method of the store's private version-history helper. Its only caller folds the tenant first.",
        ["InMemoryWorkflowStores.cs:AppendCopyOf"] =
            "Method of the store's private version-history helper. Its only caller folds the tenant first.",
    };

    private static readonly Regex MethodHeader = new(
        @"(?<mods>(?:\b(?:public|private|internal|protected|static|async|virtual|override|sealed)\s+)+)" +
        @"(?:\([^()]*\)|[\w<>\[\],\.\?\s])+?\s+(?<name>\w+)\s*\((?<parameters>[^()]*)\)\s*(?<body>\{|=>)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex TenantParameter = new(
        @"\bstring\??\s+tenantId\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ContextFieldName = new(
        @"\b(?:private|internal|protected|public)\s+(?:readonly\s+)?ITenantContext\??\s+(?<name>_?\w+)\s*[;=]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ContextParameterRead = new(
        @"(?<![_\w])tenantContext\??\s*\.\s*TenantId",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Every_store_reads_its_tenant_through_the_canonical_wrapper()
    {
        var offenders = new List<string>();
        var contextFields = 0;

        foreach (var (relative, text) in Scan())
        {
            var lines = StripComments(text).Split('\n');
            var fields = ContextFieldNames(lines);

            contextFields += fields.Count;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                foreach (var field in fields)
                {
                    if (AssignsField(line, field) && !line.Contains(FoldedWrapper, StringComparison.Ordinal))
                    {
                        offenders.Add($"{relative}:{i + 1}: {line.Trim()}");
                    }
                }

                // The constructor parameter is the raw context. Reading it anywhere but the wrapper skips the fold.
                if (ContextParameterRead.IsMatch(line))
                {
                    offenders.Add($"{relative}:{i + 1}: {line.Trim()}");
                }
            }
        }

        // Nine stores read the tenant from a context. Far fewer means the field pattern stopped matching.
        contextFields.ShouldBeGreaterThanOrEqualTo(5, "The scan found almost no ITenantContext fields; its pattern no longer matches the code.");

        Fail(
            offenders,
            "An in-memory store keeps or reads an ITenantContext without wrapping it.",
            "Assign the field `CanonicalTenantContext.Wrap(tenantContext ?? FixedTenantContext.Default)` and read the field, never the parameter.");
    }

    [Fact]
    public void Every_public_method_that_takes_a_tenant_identifier_folds_it()
    {
        var offenders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (relative, text) in Scan())
        {
            var code = StripComments(text);
            var file = Path.GetFileName(relative);
            var coveredSpans = new List<(int Start, int End)>();

            foreach (Match header in MethodHeader.Matches(code))
            {
                var parameters = header.Groups["parameters"];

                if (!TenantParameter.IsMatch(parameters.Value))
                {
                    continue;
                }

                coveredSpans.Add((parameters.Index, parameters.Index + parameters.Length));

                var name = header.Groups["name"].Value;
                var key = $"{file}:{name}";
                seen.Add(key);

                if (!Regex.IsMatch(header.Groups["mods"].Value, @"\bpublic\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    continue;
                }

                if (Exceptions.ContainsKey(key))
                {
                    continue;
                }

                var body = BodyOf(code, header.Groups["body"]);

                if (!AcceptedFolds.Any(fold => body.Contains(fold, StringComparison.Ordinal)))
                {
                    offenders.Add($"{relative}: {name}({parameters.Value.Trim()})");
                }
            }

            // A tenantId parameter this scan could not attach to a method header is a header it did not parse.
            // Failing here keeps the scan from passing because it read nothing.
            foreach (Match parameter in TenantParameter.Matches(code))
            {
                if (!coveredSpans.Exists(span => parameter.Index >= span.Start && parameter.Index < span.End))
                {
                    offenders.Add($"{relative}: a tenantId parameter outside a recognised method header, near '{Excerpt(code, parameter.Index)}'");
                }
            }
        }

        var stale = Exceptions.Keys.Where(key => !seen.Contains(key)).Select(key => $"stale exception: {key}");

        // The family has around eighty public methods that take a tenantId. Far fewer means the header pattern
        // stopped matching.
        seen.Count.ShouldBeGreaterThanOrEqualTo(30, "The scan found almost no method that takes a tenantId; its pattern no longer matches the code.");

        Fail(
            [.. offenders, .. stale],
            "A public in-memory store method takes a tenantId and does not fold it.",
            "Start the body with `tenantId = AmbientTenantScope.Normalize(tenantId);` (NormalizeOrNull where null means every tenant), or name the method in Exceptions with the reason.");
    }

    [Fact]
    public void Every_tenant_carried_by_a_query_or_request_is_folded_where_it_is_read()
    {
        var offenders = new List<string>();
        var reads = 0;

        foreach (var (relative, text) in Scan())
        {
            var lines = StripComments(text).Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match read in CarriedTenantRead.Matches(lines[i]))
                {
                    var owner = read.Groups["owner"].Value;
                    reads++;

                    var folded = lines[i].Contains($"Normalize({owner}.TenantId)", StringComparison.Ordinal)
                        || lines[i].Contains($"NormalizeOrNull({owner}.TenantId)", StringComparison.Ordinal)
                        || lines[i].Contains($"ResolveTenantId({owner}.TenantId)", StringComparison.Ordinal);

                    if (!folded)
                    {
                        offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        reads.ShouldBeGreaterThanOrEqualTo(15, "The scan found almost no query tenant; its pattern no longer matches the code.");

        Fail(
            offenders,
            "A tenant carried by a query or request object is read without being folded.",
            "Fold it on the same line: `AmbientTenantScope.Normalize(query.TenantId)` (NormalizeOrNull where null means the ambient or every tenant), or hoist it once into a local that is.");
    }

    private static IEnumerable<(string Relative, string Text)> Scan()
    {
        var root = Path.Combine(RepositoryRoot, "src", "Tracon.Core");
        var files = Directory.EnumerateFiles(root, "InMemory*.cs", SearchOption.AllDirectories).ToList();

        // A wrong root would make every scan pass. The family is thirty-odd files; far fewer means the path is wrong.
        files.Count.ShouldBeGreaterThan(25, $"The scan found only {files.Count} in-memory store files under {root}.");

        foreach (var file in files)
        {
            yield return (Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/'), File.ReadAllText(file));
        }
    }

    private static List<string> ContextFieldNames(string[] lines)
    {
        var names = new List<string>();

        foreach (var line in lines)
        {
            foreach (Match match in ContextFieldName.Matches(line))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names;
    }

    private static bool AssignsField(string line, string field)
        => Regex.IsMatch(
            line,
            $@"(?<![\w.]){Regex.Escape(field)}\s*=(?![=>])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    /// <summary>Blanks <c>//</c> comments (and so XML documentation) while keeping every offset and line.</summary>
    private static string StripComments(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var line in text.Split('\n'))
        {
            var commentAt = line.IndexOf("//", StringComparison.Ordinal);
            builder.Append(commentAt < 0 ? line : line[..commentAt].PadRight(line.Length)).Append('\n');
        }

        return builder.ToString(0, builder.Length - 1);
    }

    private static string BodyOf(string code, Group opener)
    {
        var start = opener.Index;
        var depth = 0;

        if (string.Equals(opener.Value, "{", StringComparison.Ordinal))
        {
            for (var i = start; i < code.Length; i++)
            {
                depth += code[i] == '{' ? 1 : code[i] == '}' ? -1 : 0;

                if (depth == 0)
                {
                    return code[start..(i + 1)];
                }
            }
        }
        else
        {
            for (var i = start; i < code.Length; i++)
            {
                depth += code[i] is '(' or '{' or '[' ? 1 : code[i] is ')' or '}' or ']' ? -1 : 0;

                if (depth == 0 && code[i] == ';')
                {
                    return code[start..(i + 1)];
                }
            }
        }

        return code[start..];
    }

    private static string Excerpt(string code, int index)
    {
        var start = Math.Max(0, index - 40);
        var length = Math.Min(code.Length - start, 90);

        return code.Substring(start, length).ReplaceLineEndings(" ");
    }

    private static void Fail(List<string> offenders, string problem, string remedy)
    {
        if (offenders.Count == 0)
        {
            return;
        }

        Assert.Fail(new StringBuilder()
            .AppendLine(problem)
            .AppendLine(remedy)
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, offenders))
            .ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

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
