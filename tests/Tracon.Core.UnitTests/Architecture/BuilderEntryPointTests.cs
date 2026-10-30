namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// One entry point opens the Tracon chain, and every capability after it hangs
/// off <see cref="ITraconBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// moved <c>AddJobHandler</c> and <c>UseScheduling</c> from
/// <c>IServiceCollection</c> to the chain: the site taught two entry points and a
/// consumer who typed <c>builder.AddTracon().</c> found one capability missing. A
/// public extension on <c>IServiceCollection</c> or <c>IHostApplicationBuilder</c> is
/// allowed for <c>AddTracon</c> only; anything else has to be reachable from the
/// chain it returns.
/// </para>
/// <para>
/// The test reads the <c>PublicAPI</c> files of every package under <c>src/</c>,
/// which hold the public surface line by line, so it needs no build output.
/// <c>Tracon.Client</c> has no such file; its <c>AddTraconClient</c> registers an HTTP
/// client for a remote Tracon server, not a Tracon host, and is out of scope.
/// </para>
/// </remarks>
public sealed class BuilderEntryPointTests
{
    private static readonly string[] HostReceivers =
    [
        "this Microsoft.Extensions.DependencyInjection.IServiceCollection!",
        "this Microsoft.Extensions.Hosting.IHostApplicationBuilder!",
    ];

    [Fact]
    public void Only_AddTracon_extends_the_service_collection_or_the_host_builder()
    {
        var offenders = new List<string>();

        foreach (var file in PublicApiFiles())
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!HostReceivers.Any(receiver => line.Contains($"({receiver}", StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!line.StartsWith("static Tracon.TraconServiceCollectionExtensions.AddTracon(", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(RepositoryRoot, file)}: {line}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "A public extension on IServiceCollection or IHostApplicationBuilder splits the entry point in two. "
            + "Put it on ITraconBuilder, the chain AddTracon() returns.");
    }

    [Fact]
    public void The_scan_sees_AddTracon_so_an_empty_result_cannot_pass_by_missing_the_files()
    {
        var lines = PublicApiFiles().SelectMany(File.ReadLines).ToList();

        lines.Count(static line => line.StartsWith("static Tracon.TraconServiceCollectionExtensions.AddTracon(", StringComparison.Ordinal))
            .ShouldBe(2, "AddTracon has one overload per receiver; the scan must find both.");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static IEnumerable<string> PublicApiFiles()
        => Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "PublicAPI.*.txt", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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

        throw new InvalidOperationException("Tracon.slnx was not found above the test output directory.");
    }
}
