using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Two rules for the text a consumer reads before the first agent runs: the same
/// configuration key everywhere, and the package name next to a call that comes
/// from a package.
/// </summary>
/// <remarks>
/// <para>
/// <b>One key form.</b> The template and the <c>SectionName</c> constants use
/// <c>Tracon:Providers:*</c>. Fourteen examples read <c>OpenAI:ApiKey</c>, a key
/// nothing binds: a reader who copied one got a host with no provider. The gate
/// is zero, not a ratchet: the text was fixed in the same phase, so a new
/// occurrence is a regression the moment it lands.
/// </para>
/// <para>
/// <b>The package named.</b> A page that calls an extension of a package the
/// <c>Tracon</c> meta package does not carry has to name that package, or a reader
/// with the meta package alone gets <c>CS1061</c> at the call. The mapping from a
/// call to its package is NOT written by hand: it is read from the sources
/// (<c>public static ITraconBuilder Name(this ITraconBuilder</c>) and from the meta
/// package's project references. When the meta package's composition changes, the
/// pages go red by themselves. Phase 256 moved PostgreSQL out of the meta package
/// and left nine pages calling <c>UsePostgreSql</c> without the package name; the
/// same class for any other call is closed here.
/// </para>
/// </remarks>
public sealed class ShippedDocumentationConsistencyTests
{
    private const string MetaPackage = "Tracon";

    private static readonly Regex ProviderKeyInOldForm = new(
        @"Configuration\[""(?:OpenAI|Anthropic|Google|AzureOpenAI):",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    // Multi-line on purpose: a provider's overload wraps after the name.
    private static readonly Regex BuilderExtension = new(
        @"public\s+static\s+ITraconBuilder\s+(?<name>\w+)\s*(?:<[^>]*>)?\s*\(\s*this\s+ITraconBuilder\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly string[] SkippedDirectorySegments = ["obj", "bin", "node_modules", "artifacts", "dist", "wwwroot"];

    [Fact]
    public void Shipped_text_reads_a_provider_key_only_in_the_Tracon_Providers_form()
    {
        var offenders = new List<string>();

        foreach (var file in ShippedTextFiles())
        {
            var number = 0;

            foreach (var line in File.ReadLines(file))
            {
                number++;

                if (ProviderKeyInOldForm.IsMatch(line))
                {
                    offenders.Add($"{Relative(file)}:{number}: {line.Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Provider settings live under Tracon:Providers:<Provider>: (the SectionName constants and the template bind that form). " +
            $"A key such as OpenAI:ApiKey binds nothing.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void The_call_to_package_mapping_is_read_from_the_sources()
    {
        // The rule below is only as good as its mapping; a vacuous mapping would pass every page.
        var packageOf = CallsOutsideTheMetaPackage();

        packageOf["UsePostgreSql"].ShouldBe("Tracon.PostgreSql");
        packageOf["UseSqlServer"].ShouldBe("Tracon.SqlServer");
        packageOf["UseSqlite"].ShouldBe("Tracon.Sqlite");
        packageOf["UseAnthropic"].ShouldBe("Tracon.Anthropic");
        packageOf["UseGoogle"].ShouldBe("Tracon.Google");
        packageOf["UseAzureOpenAI"].ShouldBe("Tracon.Azure");

        packageOf.ContainsKey("UseOpenAI").ShouldBeFalse("UseOpenAI comes with the meta package.");
        packageOf.ContainsKey("UseUI").ShouldBeFalse("UseUI comes with the meta package.");
    }

    [Fact]
    public void A_page_that_calls_a_package_extension_names_the_package()
    {
        var packageOf = CallsOutsideTheMetaPackage();
        var offenders = new List<string>();

        foreach (var page in ShippedPages())
        {
            var text = File.ReadAllText(page);

            foreach (var (call, package) in packageOf)
            {
                if (text.Contains($".{call}(", StringComparison.Ordinal) &&
                    !text.Contains(package, StringComparison.Ordinal))
                {
                    offenders.Add($"{Relative(page)} calls .{call}() and never names '{package}'");
                }
            }
        }

        offenders.Sort(StringComparer.Ordinal);

        offenders.ShouldBeEmpty(
            "The Tracon meta package does not carry these packages. A reader with the meta package alone " +
            $"gets CS1061 at the call unless the page names the package.{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// The <c>ITraconBuilder</c> extension methods of every package the meta package does
    /// not reach, each with the package that declares it.
    /// </summary>
    /// <remarks>
    /// A name that a package inside the meta package ALSO declares is left out: the page
    /// cannot be blamed for the wrong one of two.
    /// </remarks>
    private static Dictionary<string, string> CallsOutsideTheMetaPackage()
    {
        var inside = PackagesReachedFrom(MetaPackage);
        var declaredInside = new HashSet<string>(StringComparer.Ordinal);
        var outside = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RepoRoot.Path, "src"), "Tracon*"))
        {
            var package = Path.GetFileName(directory);

            if (!File.Exists(Path.Combine(directory, $"{package}.csproj")) || IsNotPackable(directory, package))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (IsSkipped(file))
                {
                    continue;
                }

                foreach (Match match in BuilderExtension.Matches(File.ReadAllText(file)))
                {
                    var name = match.Groups["name"].Value;

                    if (inside.Contains(package))
                    {
                        declaredInside.Add(name);
                    }
                    else if (outside.TryGetValue(name, out var existing) && !string.Equals(existing, package, StringComparison.Ordinal))
                    {
                        ambiguous.Add(name);
                    }
                    else
                    {
                        outside[name] = package;
                    }
                }
            }
        }

        foreach (var name in declaredInside.Union(ambiguous, StringComparer.Ordinal))
        {
            outside.Remove(name);
        }

        return outside;
    }

    /// <summary>The package itself and every package it reaches through project references.</summary>
    private static HashSet<string> PackagesReachedFrom(string root)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            var package = pending.Pop();

            if (!reached.Add(package))
            {
                continue;
            }

            var project = Path.Combine(RepoRoot.Path, "src", package, $"{package}.csproj");

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = (string?)reference.Attribute("Include");

                if (include is not null)
                {
                    pending.Push(Path.GetFileNameWithoutExtension(include.Replace('\\', '/')));
                }
            }
        }

        return reached;
    }

    private static bool IsNotPackable(string directory, string package)
        => XDocument.Load(Path.Combine(directory, $"{package}.csproj"))
            .Descendants("IsPackable")
            .Any(element => string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    /// <summary>The root README, every package README and the hand-written site pages (not the generated API and HTTP references).</summary>
    private static IEnumerable<string> ShippedPages()
    {
        yield return Path.Combine(RepoRoot.Path, "README.md");

        foreach (var readme in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "README.md", SearchOption.AllDirectories))
        {
            if (!IsSkipped(readme) && Relative(readme).Count(character => character == '/') == 2)
            {
                yield return readme;
            }
        }

        var site = Path.Combine(RepoRoot.Path, "docs-site", "src", "content", "docs");

        foreach (var page in Directory.EnumerateFiles(site, "*.*", SearchOption.AllDirectories))
        {
            var relative = Relative(page);

            if ((relative.EndsWith(".md", StringComparison.Ordinal) || relative.EndsWith(".mdx", StringComparison.Ordinal)) &&
                !relative.Contains("/content/docs/api/", StringComparison.Ordinal) &&
                !relative.Contains("/content/docs/http-api/", StringComparison.Ordinal))
            {
                yield return page;
            }
        }
    }

    /// <summary>Every file the key rule reads: the pages, plus the source files whose <c>///</c> comments and examples ship.</summary>
    private static IEnumerable<string> ShippedTextFiles()
    {
        foreach (var page in ShippedPages())
        {
            yield return page;
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (!IsSkipped(file))
            {
                yield return file;
            }
        }
    }

    private static bool IsSkipped(string path)
    {
        var relative = Relative(path);

        return SkippedDirectorySegments.Any(segment => relative.Contains($"/{segment}/", StringComparison.Ordinal));
    }

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot.Path, path).Replace('\\', '/');
}
