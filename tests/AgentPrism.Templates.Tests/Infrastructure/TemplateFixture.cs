using System.Text.RegularExpressions;

namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>
/// Tum sablon testlerinin paylastigi tek kullanimlik kurulum: cozumu paketler
/// (yerel bir NuGet feed'i olarak <c>artifacts/package/release</c> dolar),
/// paket surumunu cozer ve sablonu <c>dotnet new install</c> ile kurar.
/// </summary>
/// <remarks>
/// AgentPrism nuget.org'da yayinlanmadigi icin (Faz 7 beklemede, K-068) uretilen
/// projelerin <c>AgentPrism</c> paket referansi yalnizca bu yerel feed'den
/// cozulebilir. Sablonun kendi varsayilan surum degeri (<c>*-*</c>, kayan
/// on-surum) nuget.org gibi tam bir feed varsayar; test yalitimi icin burada
/// PAKETLENEN surum acikca cozulup her `dotnet new` cagrisina verilir.
/// </remarks>
public sealed class TemplateFixture : IAsyncLifetime
{
    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Cozumdeki tum AgentPrism paketlerinin paylastigi surum.</summary>
    public string Version { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var packResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{RepoPaths.PackableSolutionFilter}\" -c Release",
            timeout: PackTimeout);

        if (packResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet pack' basarisiz oldu:{Environment.NewLine}{packResult.Combined}");
        }

        Version = ResolveMetaPackageVersion();

        // Onceki bir kosudan kalmis olabilecek kaydi once kaldir - sessizce
        // eski bir surumle calismak yerine acik hata vermek tercih edilir.
        await ProcessRunner.RunAsync("dotnet", $"new uninstall \"{RepoPaths.TemplatesProjectDirectory}\"", timeout: InstallTimeout);

        var installResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"new install \"{RepoPaths.TemplatesProjectDirectory}\"",
            timeout: InstallTimeout);

        if (installResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet new install' basarisiz oldu:{Environment.NewLine}{installResult.Combined}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ProcessRunner.RunAsync("dotnet", $"new uninstall \"{RepoPaths.TemplatesProjectDirectory}\"", timeout: InstallTimeout);
    }

    /// <summary>
    /// Verilen dizine, yerel paket feed'ini (<c>artifacts/package/release</c>)
    /// nuget.org ile birlikte tanimlayan bir <c>NuGet.config</c> yazar.
    /// </summary>
    public static async Task WriteLocalNuGetConfigAsync(string directory)
    {
        var content = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="agentprism-local" value="{RepoPaths.PackageReleaseDirectory}" />
              </packageSources>
            </configuration>
            """;

        await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.config"), content);
    }

    /// <summary>
    /// <c>dotnet new agentprism-api</c> calistirir; hedef dizine paketlenen
    /// surumu isaret eden bir <c>NuGet.config</c> onceden yazilir.
    /// </summary>
    public async Task<ProcessResult> NewAsync(string name, string outputDirectory, string extraArgs = "")
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteLocalNuGetConfigAsync(outputDirectory);

        return await ProcessRunner.RunAsync(
            "dotnet",
            $"new agentprism-api -n {name} -o \"{outputDirectory}\" --AgentPrismVersion {Version} --skip-restore {extraArgs}",
            timeout: InstallTimeout);
    }

    private static readonly Regex MetaPackageFileName = new(
        @"^AgentPrism\.(?<version>\d[^.]*(?:\.[^.]*)*)\.nupkg$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <remarks>
    /// 🚨 <c>Directory.EnumerateFiles</c>'in sirasi dosya sistemine ozgudur ve
    /// <strong>surume gore SIRALI olmayabilir</strong> (olculdu). Onceki bir
    /// fazin kapanisindan kalmis eski bir <c>AgentPrism.&lt;eski-surum&gt;.nupkg</c>
    /// klasorde durursa, ilk eslesen dosya rastgele bicimde ESKI (belki
    /// analyzer'i eksik, bkz. AgentPrism.Core.csproj'daki '--no-build' notu)
    /// bir surumu secebilirdi. Aday dosyalar arasindan en SON YAZILAN secilir:
    /// bu, az once tamamlanan <c>InitializeAsync</c>'in kendi pack ciktisidir.
    /// </remarks>
    private static string ResolveMetaPackageVersion()
    {
        var match = Directory.EnumerateFiles(RepoPaths.PackageReleaseDirectory, "AgentPrism.*.nupkg")
                .Select(path => (Path: path, Match: MetaPackageFileName.Match(Path.GetFileName(path))))
                .Where(candidate => candidate.Match.Success)
                .OrderByDescending(candidate => File.GetLastWriteTimeUtc(candidate.Path))
                .Select(candidate => candidate.Match)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"'{RepoPaths.PackageReleaseDirectory}' altinda 'AgentPrism.<surum>.nupkg' bulunamadi.");

        return match.Groups["version"].Value;
    }
}
