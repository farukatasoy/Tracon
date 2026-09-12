namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every project under <c>src/</c> must declare its public API tracking
/// status explicitly: either it carries both <c>PublicAPI.Shipped.txt</c>
/// and <c>PublicAPI.Unshipped.txt</c>, or its own <c>.csproj</c> sets
/// <c>TraconPublicApiTrackingEnabled=false</c>. A third state - neither -
/// is an error.
/// </summary>
/// <remarks>
/// <c>src/Directory.Build.props</c> enables
/// <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c> for every project by
/// default, but <c>Directory.Build.targets</c> only adds the tracking files
/// as <c>AdditionalFiles</c> when they already <em>exist</em> - if a new
/// packable package is added without either declaration, the analyzer stays
/// wired up yet has nothing to check, and it silently tracks nothing (Phase
/// 96, item 96.4). This test reads project files from disk, the same
/// pattern as <see cref="DependencyDirectionTests"/> and
/// <see cref="SourceLanguageTests"/>, so it holds even before the project
/// has ever been built.
/// </remarks>
public sealed class PublicApiTrackingDeclarationTests
{
    /// <summary>
    /// Four packages are knowingly excluded (K-424): <c>Generators</c> and
    /// <c>Templates</c> ship no consumer-facing API surface, and
    /// <c>Client</c> and <c>Cli</c> have their surface generated from the
    /// OpenAPI document, which already carries its own drift gate
    /// (<c>ClientDescriptionBaselineTests</c>). This test does not special-case
    /// them - it only requires that their opt-out be the explicit
    /// <c>TraconPublicApiTrackingEnabled=false</c>, same as any other project.
    /// </summary>
    [Fact]
    public void Every_src_project_declares_its_public_API_tracking_status()
    {
        var failures = new List<string>();

        foreach (var projectDirectory in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, "src")))
        {
            var name = Path.GetFileName(projectDirectory);
            var csproj = Path.Combine(projectDirectory, $"{name}.csproj");

            if (!File.Exists(csproj))
            {
                // Tracon.Sql.Shared is a linked-source tree (K-176), not
                // its own project - it carries no .csproj and is compiled
                // into each SQL provider package instead.
                continue;
            }

            var hasShipped = File.Exists(Path.Combine(projectDirectory, "PublicAPI.Shipped.txt"));
            var hasUnshipped = File.Exists(Path.Combine(projectDirectory, "PublicAPI.Unshipped.txt"));
            var opensOut = File.ReadAllText(csproj).Contains(
                "<TraconPublicApiTrackingEnabled>false</TraconPublicApiTrackingEnabled>",
                StringComparison.Ordinal);

            switch (hasShipped && hasUnshipped, opensOut)
            {
                case (true, true):
                    failures.Add(
                        $"{name}: carries PublicAPI.Shipped/Unshipped.txt AND sets " +
                        "TraconPublicApiTrackingEnabled=false - pick one.");
                    break;
                case (false, false):
                    failures.Add(
                        $"{name}: neither carries both PublicAPI.Shipped.txt and " +
                        "PublicAPI.Unshipped.txt, nor sets TraconPublicApiTrackingEnabled=false " +
                        "in its own .csproj. Add the tracking files, or opt out explicitly.");
                    break;
                case (true, false) or (false, true):
                    break;
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found. Searched upward from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
