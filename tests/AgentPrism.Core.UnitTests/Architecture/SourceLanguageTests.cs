using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the source language boundary: everything that ships in a package or
/// runs at run time is written in English.
/// </summary>
/// <remarks>
/// <para>
/// This is a ratchet, not a snapshot. <c>source-language-baseline.txt</c> records
/// the number of offending lines that each file is still allowed to have. The test
/// fails when a file gains lines, when an unlisted file gains any, and when a file
/// loses lines without the baseline being refreshed — so the debt can only shrink.
/// </para>
/// <para>
/// Refresh the baseline after a clean-up pass:
/// <c>AGENTPRISM_SOURCE_LANGUAGE_REFRESH=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release</c>.
/// </para>
/// <para>
/// Development tooling stays Turkish and is out of scope: <c>docs/</c>,
/// <c>.agents/skills/</c> and <c>scripts/</c> are never scanned. The user-facing
/// translation dictionary <c>locales/tr.ts</c> is excluded for the same reason,
/// and the language-picker label in <c>locales/en.ts</c> is allowed by line.
/// </para>
/// <para>
/// Reads project sources from disk rather than compiled assemblies — the same
/// pattern as <see cref="DependencyDirectionTests"/> and
/// <see cref="ProblemDetailsLanguageTests"/>, which enforces the narrower K-232
/// rule and is deliberately kept alongside this one.
/// </para>
/// </remarks>
public sealed class SourceLanguageTests
{
    private const string RefreshEnvVar = "AGENTPRISM_SOURCE_LANGUAGE_REFRESH";

    private static readonly string[] ScanRoots = ["src", "tests", "samples", "packages"];

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".sql", ".csproj", ".props", ".targets", ".json", ".ts", ".tsx",
    };

    /// <summary>
    /// A package README ships inside the published package and is what the
    /// registry renders — <c>src/Directory.Build.props</c> sets
    /// <c>PackageReadmeFile</c> for NuGet, and <c>packages/agentprism-client/README.md</c>
    /// ships to npm the same way (Phase 84). Markdown is otherwise out of
    /// scope, so only these are matched.
    /// </summary>
    private static readonly Regex PackagedReadmePattern = new(
        @"^(?:src|packages)/[^/]+/README\.md$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Root files that face outward: the repository's own front page and the two
    /// files a contributor reads first. They are English for the same reason a
    /// package README is, and nothing under <see cref="ScanRoots"/> covers them.
    /// Markdown is otherwise out of scope, so only these are matched.
    /// </summary>
    private static readonly string[] ScannedRootFiles =
        ["README.md", "CONTRIBUTING.md", "ARCHITECTURE.md"];

    private static readonly string[] SkippedDirectorySegments =
        ["obj", "bin", "node_modules", "artifacts", "dist", "wwwroot"];

    /// <summary>
    /// Files excluded in full. These three test files carry the Turkish dictionary
    /// itself; the locale file is a legitimate translation dictionary (K-228).
    /// </summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/AgentPrism.Core.UnitTests/Architecture/SourceLanguageTests.cs",
        "tests/AgentPrism.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs",
        // Carries the defect-id prefix, which is also a Turkish word.
        "tests/AgentPrism.Core.UnitTests/Architecture/ShippedDocumentationSelfContainmentTests.cs",
        "src/AgentPrism.UI/frontend/src/locales/tr.ts",
        // Phase 109 split the monolithic tr.ts into domain fragments — same
        // K-228 status, still an aggregate-only file with no dictionary text
        // of its own once split. Each fragment carries the exemption instead.
        "src/AgentPrism.UI/frontend/src/locales/tr/common.ts",
        "src/AgentPrism.UI/frontend/src/locales/tr/agents.ts",
        "src/AgentPrism.UI/frontend/src/locales/tr/runs.ts",
        "src/AgentPrism.UI/frontend/src/locales/tr/workflows.ts",
        "src/AgentPrism.UI/frontend/src/locales/tr/operations.ts",
        "src/AgentPrism.UI/frontend/src/locales/tr/settings.ts",
        // The embeddable widget's own small dictionary (Phase 61) is deliberately
        // separate from the console's locales/ — same K-228 status, split into
        // its own file (not embed/locale.ts, which also carries English) so this
        // exemption stays as narrow as the console's.
        "src/AgentPrism.UI/frontend/src/embed/locale.tr.ts",
    ];

    /// <summary>
    /// The language picker labels its own entry in its own language, and the
    /// localization end-to-end test asserts the actual translated heading
    /// text from <c>locales/tr.ts</c> (K-228) — both are legitimate Turkish.
    /// </summary>
    private static readonly Regex AllowedLinePattern = new(
        @"shell\.language\.tr|Gösterge Paneli",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Turkish-named identifiers that are not prose and must survive translation:
    /// defect ids (<c>HATA-S3-008</c>), manual test case ids (<c>MT-MCP-023</c>),
    /// and paths into the development tooling, which stays Turkish by design
    /// (<c>docs/arsiv/fazlar/21-KOTA-VE-OLAY-YAYINI.md</c>, <c>scripts/dokuman-bakim.py</c>).
    /// They are removed from a line before it is inspected — otherwise
    /// <c>HATA-</c> would forever match the word <c>hata</c> and the baseline
    /// could never reach zero.
    /// </summary>
    private static readonly Regex IdentifierMaskPattern = new(
        @"HATA-[A-Za-z0-9-]+|MT-[A-Za-z0-9-]+|[A-Za-z0-9_./-]+\.(?:md|py|sh)\b|\.agents/skills/[A-Za-z0-9_/-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Letters that exist in Turkish but not in English.</summary>
    private static readonly Regex TurkishLetterPattern = new(
        "[çğıöşüÇĞİÖŞÜ]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Turkish words written without Turkish letters — the dominant form in this
    /// repository. Matching is whole-word and case-insensitive.
    /// </summary>
    /// <remarks>
    /// Words that are also English words, or that appear inside identifiers, are
    /// deliberately absent: <c>her</c>, <c>son</c>, <c>alt</c>, <c>rol</c>,
    /// <c>ise</c>, <c>gibi</c>, <c>ama</c>, <c>para</c> (an XML doc tag),
    /// <c>once</c>, <c>var</c>, <c>tek</c>, and every two-letter word. The list
    /// below is wide enough that a Turkish sentence practically cannot avoid it.
    /// </remarks>
    private static readonly string[] TurkishWords =
    [
        // connectives, pronouns, adverbs
        "bir", "ile", "icin", "ayni", "olarak", "yalniz", "yalnizca", "hicbir",
        "veya", "kendi", "zaten", "burada", "yoksa", "yuzden", "icinde",
        "sessizce", "yeniden", "ancak", "ayrica", "boylece", "cunku", "ustelik",
        "yani", "ornegin", "kadar", "sonra", "simdi", "artik",
        // negation and existence
        "degil", "degildir", "yoktur", "vardir", "olmaz", "olamaz", "olmalidir",
        "gerekir", "gerekmez", "edilmez", "yapilmaz", "yazilmaz", "kullanilmaz",
        // domain nouns
        "bos", "yeni", "eski", "zaman", "varsayilan", "deger", "gerekce",
        "denetim", "gercek", "kayit", "kaydi", "kayitli", "cagri", "cagrisi",
        "kimlik", "kimligi", "kiraci", "kiracinin", "saglayici", "saglayicinin",
        "calistirma", "calistirmanin", "hata", "hatasi", "iptal", "kaynak",
        "satir", "sutun", "metin", "istek", "yanit", "karar", "onay", "oturum",
        "depo", "deposu", "sorgu", "tablo", "sema", "surum", "sayisi", "sayaci",
        "listesi", "kume", "kumesi", "ornek", "ornegi", "dosya", "dosyasi",
        "klasor", "dizin", "anahtar", "anahtari", "adres", "baglanti", "agac",
        "dugum", "olay", "olayi", "akis", "kuyruk", "gorev", "islem", "islemi",
        "katman", "arayuz", "sozlesme", "sinir", "esik", "bayrak",
        "yapilandirma", "guvenlik", "yetki", "izin", "gizli", "parola", "sifre",
        // verbs
        "uretir", "doner", "donerse", "yazilir", "okunur", "olusturur", "tasir",
        "dogrular", "kullanilir", "cagirir", "baslatir", "durdurur", "siler",
        "ekler", "gunceller", "secer", "bulur", "atar", "alir", "verir",
        "gecer", "gecirir", "bekler", "kapatir", "acar", "yapar", "eder",
        // adjectives and numerals
        "acik", "kapali", "iki", "dort", "bes", "tum", "tumu", "ilk", "geri",
        "ileri", "ust", "ayri", "ortak", "zorunlu", "istege", "bagli",
        "gecersiz", "gecerli", "eksik", "fazla", "buyuk", "kucuk", "uzun",
        "kisa", "dogru", "yanlis", "onceki", "sonraki",
        // project jargon
        "faz", "bkz", "kez", "adi", "adinda", "sirasi", "sirayla", "derleyici",
        "sarmalayici", "dekorator", "defter", "defteri", "kapi", "kapisi",
    ];

    private static readonly Regex TurkishWordPattern = BuildWordPattern();

    [Fact]
    public void Source_tree_carries_no_Turkish_outside_the_baseline()
    {
        var actual = Scan();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(actual);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var (path, count) in actual)
        {
            var allowed = baseline.GetValueOrDefault(path, 0);

            if (count > allowed)
            {
                failures.Add($"+ {path}: {count} offending lines, baseline allows {allowed}");
            }
        }

        foreach (var (path, allowed) in baseline)
        {
            var count = actual.GetValueOrDefault(path, 0);

            if (count < allowed)
            {
                failures.Add($"- {path}: {count} offending lines, baseline still allows {allowed} — refresh it");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The source language baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"Lines that only lost debt are fixed by refreshing: {RefreshEnvVar}=1 " +
                           "dotnet test tests/AgentPrism.Core.UnitTests -c Release");
    }

    private static SortedDictionary<string, int> Scan()
    {
        var results = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var root in ScanRoots)
        {
            var absoluteRoot = Path.Combine(RepositoryRoot, root);

            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');

                if (!ScannedExtensions.Contains(Path.GetExtension(file)) &&
                    !PackagedReadmePattern.IsMatch(relative))
                {
                    continue;
                }

                if (IsSkipped(relative))
                {
                    continue;
                }

                var count = CountOffendingLines(file);

                if (count > 0)
                {
                    results[relative] = count;
                }
            }
        }

        foreach (var name in ScannedRootFiles)
        {
            var file = Path.Combine(RepositoryRoot, name);

            if (!File.Exists(file))
            {
                continue;
            }

            var count = CountOffendingLines(file);

            if (count > 0)
            {
                results[name] = count;
            }
        }

        return results;
    }

    private static bool IsSkipped(string relativePath)
    {
        if (SkippedFiles.Contains(relativePath, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (var segment in SkippedDirectorySegments)
        {
            if (relativePath.Contains($"/{segment}/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountOffendingLines(string file)
    {
        var count = 0;

        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0 || AllowedLinePattern.IsMatch(line))
            {
                continue;
            }

            var inspected = IdentifierMaskPattern.Replace(line, string.Empty);

            if (TurkishLetterPattern.IsMatch(inspected) || TurkishWordPattern.IsMatch(inspected))
            {
                count++;
            }
        }

        return count;
    }

    private static Regex BuildWordPattern()
    {
        var alternatives = TurkishWords
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(word => word.Length)
            .ThenBy(word => word, StringComparer.Ordinal);

        return new Regex(
            $@"\b(?:{string.Join('|', alternatives)})\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(5));
    }

    private static Dictionary<string, int> ReadBaseline()
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.LastIndexOf('|');

            separator.ShouldBeGreaterThan(0, $"Malformed baseline line: '{line}'");

            baseline[line[..separator]] = int.Parse(
                line[(separator + 1)..],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return baseline;
    }

    private static void WriteBaseline(SortedDictionary<string, int> actual)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Generated by SourceLanguageTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release");
        builder.AppendLine("# One line per file: <path>|<offending line count>. The count may only shrink.");
        builder.AppendLine("# An empty list is the goal; keep the file even when empty.");

        foreach (var (path, count) in actual)
        {
            builder.Append(path).Append('|').Append(count).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "AgentPrism.Core.UnitTests",
        "Architecture",
        "source-language-baseline.txt");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
