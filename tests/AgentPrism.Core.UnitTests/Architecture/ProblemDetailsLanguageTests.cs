using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// K-232'yi zorlar: <c>ProblemDetails</c> <c>title</c> metinleri Ingilizce kalir.
/// </summary>
/// <remarks>
/// <para>
/// HATA-S4-007 (MT-UI-032, Aile N): sunucunun ProblemDetails basliklari koda
/// gomulu sabit Turkce'ydi — 113 farkli <c>title:</c> literali, 26 dosya.
/// K-232 zaten bunu yasaklar ("ProblemDetails metinleri ... Ingilizce kalir");
/// bu test o kurali kaynak taramasiyla kalici kilar.
/// </para>
/// <para>
/// Testler proje kaynagini okur, derleme ciktisini degil — <see cref="DependencyDirectionTests"/>
/// ile ayni desen. ASCII-Turkce sozcuk kalibi yalnizca <c>title:</c> literalinin
/// GOVDESINDE aranir; kod yorumlari (ki proje konvansiyonu geregi Turkce kalir)
/// bu regex'e hic girmez cunku <c>title:\s*"..."</c> kalibi yalniz gercek
/// ProblemDetails cagrilarinda gorunur.
/// </para>
/// </remarks>
public sealed class ProblemDetailsLanguageTests
{
    private static readonly Regex TitleLiteralPattern = new(
        "title:\\s*\"(?<title>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AsciiTurkishMarkerPattern = new(
        "(?i:\\b\\w*(?:bulunamadi|gecersiz|zorunlu|desteklenmiyor|eksik|kullanimda|basarisiz|kapali|olamaz|olmalidir|adinda|kimlikli|yetersiz|edilemedi|uyusmuyor|reddedildi|dogrulanamadi|catismasi|calistirilamadi|derlenemedi|engellendi)\\w*\\b)",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Her_ProblemDetails_title_literali_Ingilizcedir()
    {
        var root = RepositoryRoot;
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);

            foreach (Match match in TitleLiteralPattern.Matches(content))
            {
                var title = match.Groups["title"].Value;

                if (AsciiTurkishMarkerPattern.IsMatch(title))
                {
                    violations.Add($"{Path.GetRelativePath(root, file)}: title: \"{title}\"");
                }
            }
        }

        violations.ShouldBeEmpty(customMessage: string.Join('\n', violations));
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
