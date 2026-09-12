using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// The registration entry points of the public API: what a consumer calls to
/// turn a capability on.
/// </summary>
/// <remarks>
/// <para>
/// Two gates read this set — <see cref="CapabilityCoverageTests"/> asks whether
/// the capability map names the member, and <see cref="CapabilityExampleTests"/>
/// asks whether the member shows how it is called. They must agree on what
/// counts as an entry point, so the parser lives here and not in either of
/// them.
/// </para>
/// <para>
/// The scope is decision K-509: every <c>Add</c>, <c>Use</c>, or <c>Map</c>
/// member whose receiver is one of the registration types, plus every member
/// declared on the builder interface itself.
/// </para>
/// </remarks>
internal static class CapabilityEntryPoints
{
    /// <remarks>
    /// 🚨 Declared FIRST and deferred: static initialisers run in declaration
    /// order, so a repository root initialised after the collectors below is
    /// still null when they read it.
    /// </remarks>
    private static readonly Lazy<string> LazyRepositoryRoot = new(FindRepositoryRoot);

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    public static string RepositoryRoot => LazyRepositoryRoot.Value;

    /// <summary>
    /// The receivers that make an extension method a capability entry point. A
    /// consumer turns a capability on through one of these; an extension on any
    /// other type is a helper, not a registration.
    /// </summary>
    private static readonly HashSet<string> RegistrationReceivers = new(StringComparer.Ordinal)
    {
        "Tracon.ITraconBuilder",
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder",
        "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
        "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
    };

    /// <summary>A member declared on the builder interface itself.</summary>
    private static readonly Regex BuilderMemberPattern = new(
        @"^Tracon\.ITraconBuilder\.(?<name>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>An <c>Add</c>, <c>Use</c>, or <c>Map</c> extension method, with its receiver.</summary>
    private static readonly Regex ExtensionMethodPattern = new(
        @"^static [A-Za-z0-9_.]+\.(?<name>(?:Add|Use|Map)[A-Za-z0-9_]*)(?:<[^(]*>)?\(this (?<receiver>[A-Za-z0-9_.]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Every entry point name, with the packages that declare it.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ByPackage { get; } = Collect();

    /// <summary>Every entry point name.</summary>
    public static IReadOnlyCollection<string> Names { get; } = ByPackage.Keys.ToList();

    /// <summary>Every package that declares at least one entry point.</summary>
    public static IReadOnlySet<string> Packages { get; } =
        ByPackage.Values.SelectMany(packages => packages).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every public member name in the tracked API, whichever package declares it.</summary>
    /// <remarks>
    /// Wider than <see cref="Names"/> on purpose: it is what
    /// <see cref="CapabilityExampleTests"/> checks an example's calls against,
    /// and an example is free to show a member that is not itself an entry
    /// point.
    /// </remarks>
    public static IReadOnlySet<string> AllMemberNames { get; } = CollectAllMemberNames();

    /// <summary>
    /// Reads the tracked public API of every package. Both files are read:
    /// members move from <c>Unshipped</c> to <c>Shipped</c> at release, and the
    /// gates must not go blind on that day.
    /// </summary>
    private static SortedDictionary<string, IReadOnlySet<string>> Collect()
    {
        var found = new SortedDictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var file in PublicApiFiles())
        {
            // src/<package>/PublicAPI.*.txt - the directory is the package name.
            var package = Path.GetFileName(Path.GetDirectoryName(file))!;

            foreach (var line in File.ReadLines(file))
            {
                var name = EntryPointName(line);

                if (name is null)
                {
                    continue;
                }

                if (!found.TryGetValue(name, out var packages))
                {
                    packages = new HashSet<string>(StringComparer.Ordinal);
                    found[name] = packages;
                }

                ((HashSet<string>)packages).Add(package);
            }
        }

        return found;
    }

    private static string? EntryPointName(string line)
    {
        var member = BuilderMemberPattern.Match(line);

        if (member.Success)
        {
            return member.Groups["name"].Value;
        }

        var extension = ExtensionMethodPattern.Match(line);

        return extension.Success && RegistrationReceivers.Contains(extension.Groups["receiver"].Value)
            ? extension.Groups["name"].Value
            : null;
    }

    /// <summary>
    /// Every identifier that is declared as a member in the tracked API. The
    /// line shapes vary (<c>static X.Y(...)</c>, <c>X.Y.get -&gt; T</c>,
    /// <c>X.Y(...) -&gt; T</c>), so the last dotted segment before a
    /// parenthesis, an arrow, or the end of the line is taken.
    /// </summary>
    private static HashSet<string> CollectAllMemberNames()
    {
        var pattern = new Regex(
            @"\.(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^(]*>)?(?:\(|\s*->|\.get|\.set|$)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in PublicApiFiles())
        {
            foreach (var line in File.ReadLines(file))
            {
                foreach (Match match in pattern.Matches(line))
                {
                    names.Add(match.Groups["name"].Value);
                }
            }
        }

        return names;
    }

    private static IEnumerable<string> PublicApiFiles()
        => Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"),
            "PublicAPI.*.txt",
            SearchOption.AllDirectories);

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
