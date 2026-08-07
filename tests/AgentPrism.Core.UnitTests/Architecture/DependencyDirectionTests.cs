using System.Xml.Linq;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// AgentPrism'in katman mimarisini zorlayan testler.
///
/// Bagimlilik grafigi TEK YONLUDUR ve dongu icermez:
///
///     Abstractions -- Core -- PostgreSql
///                       |  -- OpenAI
///                       |  -- Mcp
///                       +----- AspNetCore -- UI
///                       |         |            |
///                       |         |     AgentPrism (meta)
///                       +----- Testing (test yardimcisi; meta pakete BAGLANMAZ)
///
/// Bu grafigi bozan bir ProjectReference eklemek yasaktir.
/// Gerekce: docs/MIMARI.md, bolum 2.
///
/// Testler proje dosyalarini okur, derleme ciktisini degil. Boylece kural
/// urun kodu yazilmadan once de gecerlidir ve derleme sirasina bagli degildir.
/// </summary>
public sealed class DependencyDirectionTests
{
    /// <summary>
    /// Her paketin referans vermesine izin verilen AgentPrism paketleri.
    /// Burada olmayan her kenar ihlaldir.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["AgentPrism.Abstractions"] = [],
        ["AgentPrism.Core"] = ["AgentPrism.Abstractions"],
        ["AgentPrism.PostgreSql"] = ["AgentPrism.Core"],
        ["AgentPrism.OpenAI"] = ["AgentPrism.Core"],
        // Anthropic ve Google da yalnizca Core'a baglidir; birbirlerini ve
        // OpenAI'i gormezler. Her saglayici paketi kendi SDK'sini izole tutar.
        ["AgentPrism.Anthropic"] = ["AgentPrism.Core"],
        ["AgentPrism.Google"] = ["AgentPrism.Core"],
        ["AgentPrism.Azure"] = ["AgentPrism.Core"],
        // Voice bir MODEL saglayicisi degildir ama ayni yalitim kuralina uyar:
        // yalnizca Core'a baglidir ve HICBIR NuGet paketi almaz (ham HttpClient).
        ["AgentPrism.Voice"] = ["AgentPrism.Core"],
        // Mcp yalnizca Core'a baglidir: HTTP katmani MCP tazelemesini
        // IMcpToolRefresher soyutlamasi uzerinden tetikler, ters yonde bir
        // referans YOKTUR. Boylece MCP istege bagli bir paket olarak kalir.
        ["AgentPrism.Mcp"] = ["AgentPrism.Core"],
        // Workflows da yalnizca Core'a baglidir: HTTP katmani workflow'lari
        // IWorkflowRunner soyutlamasi uzerinden calistirir ve bu pakete
        // referans VERMEZ. MCP ile birebir ayni desen.
        ["AgentPrism.Workflows"] = ["AgentPrism.Core"],
        ["AgentPrism.AspNetCore"] = ["AgentPrism.Core"],
        ["AgentPrism.UI"] = ["AgentPrism.AspNetCore"],
        // Testing test-yardimci paketidir: meta pakete BAGLANMAZ (bolum 39.1).
        // AspNetCore'a baglanir cunku tuketicinin en cok isteyecegi tip bellek
        // ici host fixture'idir ve o, uclari kuran paketi gerektirir.
        ["AgentPrism.Testing"] = ["AgentPrism.Core", "AgentPrism.AspNetCore"],
        ["AgentPrism"] = ["AgentPrism.AspNetCore", "AgentPrism.Mcp", "AgentPrism.OpenAI", "AgentPrism.PostgreSql", "AgentPrism.UI", "AgentPrism.Workflows"],
    };

    [Fact]
    public void Her_paket_yalnizca_izin_verilen_paketlere_referans_verir()
    {
        foreach (var (package, allowed) in AllowedReferences)
        {
            var actual = ReadAgentPrismProjectReferences(package).Order(StringComparer.Ordinal).ToList();
            var expected = allowed.Order(StringComparer.Ordinal).ToList();

            actual.ShouldBe(
                expected,
                customMessage: $"'{package}' paketinin AgentPrism referanslari beklenenden farkli. " +
                               "Katman mimarisi degistiyse once docs/MIMARI.md ve bu testi guncelleyin.");
        }
    }

    [Fact]
    public void Abstractions_hicbir_AgentPrism_paketine_referans_vermez()
    {
        // Abstractions saf sozlesme katmanidir. Kendi ailesinden hicbir sey bilmez.
        ReadAgentPrismProjectReferences("AgentPrism.Abstractions").ShouldBeEmpty();
    }

    [Fact]
    public void Bagimlilik_grafigi_dongu_icermez()
    {
        var graph = AllowedReferences.Keys.ToDictionary(
            package => package,
            ReadAgentPrismProjectReferences,
            StringComparer.Ordinal);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var package in graph.Keys)
        {
            var cycle = FindCycle(package, graph, visiting, visited, []);
            cycle.ShouldBeNull($"Bagimlilik dongusu bulundu: {string.Join(" -> ", cycle ?? [])}");
        }
    }

    [Fact]
    public void Mcp_istemci_paketi_sunucu_paketlerine_bagli_degildir()
    {
        // Faz 50: AgentPrism.AspNetCore MCP/A2A SUNUCUSU olarak disa acildi ve
        // ModelContextProtocol.AspNetCore + Microsoft.Agents.AI.Hosting.A2A +
        // Microsoft.Agents.AI.Hosting.AspNetCore + A2A.AspNetCore paketlerini aldi.
        // K-057'nin bagimlilik yonu bozulmamalidir: bu paketler yalnizca
        // AgentPrism.AspNetCore icindedir; AgentPrism.Mcp (istemci) `.Core`
        // hattinda kalir ve bunlarin HICBIRINI almaz.
        var forbidden = new[]
        {
            "ModelContextProtocol.AspNetCore",
            "Microsoft.Agents.AI.Hosting.A2A",
            "Microsoft.Agents.AI.Hosting.AspNetCore",
            "A2A.AspNetCore",
        };

        var projectPath = Path.Combine(RepositoryRoot, "src", "AgentPrism.Mcp", "AgentPrism.Mcp.csproj");
        var references = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToList();

        foreach (var name in forbidden)
        {
            references.Any(reference => string.Equals(reference, name, StringComparison.Ordinal)).ShouldBeFalse(
                $"AgentPrism.Mcp '{name}' paketini almamali; sunucu bagimliligi " +
                "yalnizca AgentPrism.AspNetCore icinde kalmalidir (K-057).");
        }
    }

    [Fact]
    public void Her_yayinlanabilir_paket_NuGet_icin_README_icerir()
    {
        // Directory.Build.targets icindeki AgentPrismValidatePackageReadme hedefi
        // bunu build sirasinda da zorlar. Test, kuralin sebebini belgeler.
        foreach (var package in AllowedReferences.Keys)
        {
            var readme = Path.Combine(RepositoryRoot, "src", package, "README.md");

            File.Exists(readme).ShouldBeTrue(
                $"'{package}' paketinde README.md yok. Bu dosya NuGet.org paket sayfasinda gorunur.");
        }
    }

    private static IReadOnlyList<string> ReadAgentPrismProjectReferences(string package)
    {
        var projectPath = Path.Combine(RepositoryRoot, "src", package, $"{package}.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Proje dosyasi bulunamadi: {projectPath}");

        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .Where(name => name.StartsWith("AgentPrism", StringComparison.Ordinal))
            .ToList();
    }

    private static List<string>? FindCycle(
        string package,
        Dictionary<string, IReadOnlyList<string>> graph,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> path)
    {
        if (visited.Contains(package))
        {
            return null;
        }

        if (!visiting.Add(package))
        {
            return [.. path, package];
        }

        path.Add(package);

        foreach (var dependency in graph.GetValueOrDefault(package, []))
        {
            var cycle = FindCycle(dependency, graph, visiting, visited, path);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(package);
        visited.Add(package);

        return null;
    }

    /// <summary>
    /// Depo kokunu bulur. Test derleme ciktisi artifacts/ altinda oldugu icin
    /// sabit bir goreli yol kullanilamaz; AgentPrism.slnx dosyasi aranarak yukari yurunur.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Depo koku bulunamadi. '{AppContext.BaseDirectory}' konumundan yukari dogru AgentPrism.slnx arandi.");
    }
}
