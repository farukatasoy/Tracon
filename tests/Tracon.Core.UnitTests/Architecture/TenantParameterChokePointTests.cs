using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every <c>tenant_id</c> command parameter is bound through the canonical
/// choke point (phase 179).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The rule: the tenant identifier is matched case-insensitively by
/// normalizing the <em>value</em>. A store that binds the raw value is
/// case-sensitive while the rest of the product is not — the same tenant then
/// reaches two different row sets on PostgreSQL and SQLite, and two tenants
/// reach one row on SQL Server's case-insensitive default collation.
/// </para>
/// <para>
/// This is a gate, not a snapshot, and it exists because the conversion that
/// introduced the rule touched 155 call sites at once. Converting 155 sites by
/// hand makes forgetting the 156th the likeliest way this rule dies — the same
/// class of defect as a hand-repeated sum gaining a term (K-483). The gate
/// costs one text scan and closes it permanently.
/// </para>
/// <para>
/// Accepted forms: <c>DbHelpers.AddTenant(...)</c>, which normalizes, or an
/// explicit <see cref="AmbientTenantScope.Normalize"/> /
/// <see cref="AmbientTenantScope.NormalizeOrNull"/> on the same line for the
/// nullable and provider-specific parameter shapes.
/// </para>
/// </remarks>
public sealed class TenantParameterChokePointTests
{
    private static readonly string[] ScanRoots =
    [
        Path.Combine("src", "Tracon.Sql.Shared"),
        Path.Combine("src", "Tracon.PostgreSql"),
        Path.Combine("src", "Tracon.SqlServer"),
        Path.Combine("src", "Tracon.Sqlite"),
    ];

    /// <summary>
    /// A line that names the <c>tenant_id</c> parameter must also fold it.
    /// </summary>
    private static readonly Regex TenantParameterLine = new(
        "\"tenant_id\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The forms that carry the fold. <c>AddTenant</c> applies it inside;
    /// the others apply it on the line itself.
    /// </summary>
    private static readonly string[] AcceptedForms =
    [
        "AddTenant(",
        "AmbientTenantScope.Normalize(",
        "AmbientTenantScope.NormalizeOrNull(",
    ];

    /// <summary>
    /// The one construct that names the column without binding a value to it:
    /// the run query's column descriptor, which says which table a column is
    /// read from. Kept as a named exception rather than by narrowing the scan
    /// to today's binding shapes — a gate that only knows the shapes it was
    /// written against misses the next one.
    /// </summary>
    private static readonly string[] NotParameterBindings =
    [
        "RunColumnSource.",
    ];

    [Fact]
    public void No_store_binds_a_raw_tenant_id_parameter()
    {
        var offenders = new List<string>();

        foreach (var root in ScanRoots)
        {
            var absoluteRoot = Path.Combine(RepositoryRoot, root);

            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');
                var lines = File.ReadAllLines(file);

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    if (!TenantParameterLine.IsMatch(line) || IsAllowed(line))
                    {
                        continue;
                    }

                    offenders.Add($"{relative}:{i + 1}: {line.Trim()}");
                }
            }
        }

        if (offenders.Count == 0)
        {
            return;
        }

        var message = new StringBuilder()
            .AppendLine("A tenant_id command parameter is bound without the canonical fold.")
            .AppendLine("Use DbHelpers.AddTenant(command, value), or AmbientTenantScope.Normalize /")
            .AppendLine("NormalizeOrNull on the same line when the parameter shape needs it.")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, offenders))
            .ToString();

        Assert.Fail(message);
    }

    private static bool IsAllowed(string line)
    {
        // A comment or an XML doc line may name the parameter without binding it.
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith('*'))
        {
            return true;
        }

        foreach (var form in AcceptedForms)
        {
            if (line.Contains(form, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var form in NotParameterBindings)
        {
            if (line.Contains(form, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
