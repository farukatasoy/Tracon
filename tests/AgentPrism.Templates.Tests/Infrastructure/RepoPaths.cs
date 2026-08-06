namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Depo kokune gore onemli yollari cozer.</summary>
internal static class RepoPaths
{
    /// <summary>
    /// Depo koku. <c>AppContext.BaseDirectory</c>'den yukari dogru <c>AgentPrism.slnx</c>
    /// bulunana kadar cikilir; <c>UseArtifactsOutput</c> nedeniyle test derlemesinin
    /// ciktisi depo kokunden uzakta bir <c>artifacts/bin/...</c> altina duser.
    /// </summary>
    public static string Root { get; } = FindRoot();

    public static string TemplatesProjectDirectory => Path.Combine(Root, "src", "AgentPrism.Templates");

    public static string SolutionFile => Path.Combine(Root, "AgentPrism.slnx");

    public static string PackageReleaseDirectory => Path.Combine(Root, "artifacts", "package", "release");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentPrism.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"AgentPrism.slnx bulunamadi. Arama '{AppContext.BaseDirectory}' dizininden yukari dogru yapildi.");
        }

        return dir.FullName;
    }
}
