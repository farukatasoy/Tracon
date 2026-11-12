using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

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
/// <c>TRACON_SOURCE_LANGUAGE_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// <para>
/// The tooling in the public tree is English too: <c>scripts/</c>, <c>.github/workflows</c>
/// and the root build and editor configuration are scanned, comments and test names included.
/// The development record that does not ship stays Turkish and is never scanned.
/// The user-facing translation dictionary <c>locales/tr.ts</c> is excluded because it is a
/// translation dictionary, and the language-picker label in <c>locales/en.ts</c> is allowed by line.
/// </para>
/// <para>
/// Reads project sources from disk rather than compiled assemblies — the same
/// pattern as <see cref="DependencyDirectionTests"/> and
/// <see cref="ProblemDetailsLanguageTests"/>, which enforces the narrower
/// rule and is deliberately kept alongside this one.
/// </para>
/// </remarks>
public sealed class SourceLanguageTests
{
    private const string RefreshEnvVar = "TRACON_SOURCE_LANGUAGE_REFRESH";

    private static readonly string[] ScanRoots =
        ["src", "tests", "samples", "bench", "packages", "scripts", ".github", "docs-site/src", "docs-site/public", "docs-site/scripts"];

    private static readonly HashSet<string> ScannedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".sql", ".csproj", ".props", ".targets", ".json", ".ts", ".tsx",
        ".md", ".mdx", ".astro", ".mjs", ".yml", ".yaml", ".txt", ".py", ".tsv", ".sh",
    };

    /// <summary>
    /// A package README ships inside the published package and is what the
    /// registry renders — <c>src/Directory.Build.props</c> sets
    /// <c>PackageReadmeFile</c> for NuGet, and <c>packages/tracon-client/README.md</c>
    /// ships to npm the same way. Markdown is otherwise out of
    /// scope, so only these are matched.
    /// </summary>
    private static readonly Regex PackagedReadmePattern = new(
        @"^(?:src|packages)/[^/]+/README\.md$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Root files that face outward: the repository's own front page, the two
    /// files a contributor reads first, and the licence file. They are
    /// English for the same reason a package README is, and nothing under
    /// <see cref="ScanRoots"/> covers them. Markdown is otherwise out of scope,
    /// so only these are matched.
    /// </summary>
    /// <remarks>
    /// The licence file is not merely outward facing — every published package
    /// carries it at its root, which makes it a shipped artifact in the same
    /// sense a package README is.
    /// </remarks>
    private static readonly string[] ScannedRootFiles =
        [
        "README.md", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md", "ARCHITECTURE.md", "LICENSE.md",
        ".editorconfig", ".gitattributes", ".gitignore", "Directory.Build.props", "Directory.Build.targets",
        "Directory.Packages.props", "NuGet.config", "global.json",
    ];

    private static readonly string[] SkippedDirectorySegments =
        ["obj", "bin", "node_modules", "artifacts", "dist", "wwwroot"];

    /// <summary>
    /// Trees that the site build generates and git ignores: the DocFX API pages,
    /// the HTTP reference and the copied OpenAPI document. Their text comes from
    /// sources this test already scans (XML documentation and endpoint metadata
    /// in <c>src</c>), so scanning them adds no coverage. It did add local state:
    /// DocFX writes the absolute source path of each test, and a worktree named
    /// after a phase puts a listed word into that path. The result then depended
    /// on whether the site was built before the tests.
    /// </summary>
    private static readonly string[] GeneratedSitePrefixes =
    [
        "docs-site/src/content/docs/api/",
        "docs-site/src/content/docs/http-api/",
        "docs-site/public/openapi/",
    ];

    /// <summary>
    /// Files excluded in full. These three test files carry the Turkish dictionary
    /// itself; the locale file is a legitimate translation dictionary.
    /// </summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/Tracon.Core.UnitTests/Architecture/SourceLanguageTests.cs",
        "tests/Tracon.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs",
        "src/Tracon.UI/frontend/src/locales/tr.ts",
        // The monolithic tr.ts was split into domain fragments. It keeps its status as
        // the Turkish dictionary but is now an aggregate-only file with no dictionary text
        // of its own. Each fragment carries the exemption instead.
        "src/Tracon.UI/frontend/src/locales/tr/common.ts",
        "src/Tracon.UI/frontend/src/locales/tr/agents.ts",
        "src/Tracon.UI/frontend/src/locales/tr/runs.ts",
        "src/Tracon.UI/frontend/src/locales/tr/workflows.ts",
        "src/Tracon.UI/frontend/src/locales/tr/operations.ts",
        "src/Tracon.UI/frontend/src/locales/tr/settings.ts",
        // The embeddable widget's own small dictionary is deliberately
        // separate from the console's locales/ — same status, split into
        // its own file (not embed/locale.ts, which also carries English) so this
        // exemption stays as narrow as the console's.
        "src/Tracon.UI/frontend/src/embed/locale.tr.ts",
    ];

    /// <summary>
    /// The language picker labels its own entry in its own language, and the
    /// localization end-to-end test asserts the actual translated heading
    /// text from <c>locales/tr.ts</c> — both are legitimate Turkish.
    /// </summary>
    private static readonly Regex AllowedLinePattern = new(
        @"shell\.language\.tr|Gösterge Paneli",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The one explicit, line-bound exception for the tooling: a test that proves a RENAMED command or flag
    /// is rejected has to contain the old Turkish name, and so does the table that lets a release gate read a
    /// tag cut before a rename. Such a line ends in a comment that says <c># legacy-name</c>. The marker is
    /// honoured only under <see cref="LegacyNameRoots"/>, only as the trailing comment, and only up to
    /// <see cref="LegacyNameLineCeiling"/> lines in the whole tree, which can only shrink.
    /// </summary>
    private static readonly Regex LegacyNameMarkerPattern = new(
        @"#\s*legacy-name\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly string[] LegacyNameRoots = ["scripts/", ".github/"];

    private const int LegacyNameLineCeiling = 20;

    /// <summary>
    /// Turkish-named identifiers that are not prose and must survive translation:
    /// defect ids and manual test case ids (a prefix, letters and a number),
    /// and the names of the repository's own scripts.
    /// They are removed from a line before it is inspected — otherwise
    /// <c>HATA-</c> would forever match the word <c>hata</c> and the baseline
    /// could never reach zero.
    /// </summary>
    /// <remarks>
    /// A markdown file name is deliberately NOT masked: a document named in Turkish is exactly
    /// the pointer a reader cannot follow, and hiding it hid the sentence around it too.
    /// </remarks>
    private static readonly Regex IdentifierMaskPattern = new(
        @"HATA-[A-Za-z0-9-]+|HATA_[A-Z0-9]+_[0-9]+|MT-[A-Za-z0-9-]+|[A-Za-z0-9_./-]+\.(?:py|sh)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Letters that exist in Turkish but not in English.</summary>
    private static readonly Regex TurkishLetterPattern = new(
        "[çğıöşüÇĞİÖŞÜ]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A Turkish letter spelled as an escape (<c>\u0131</c>, <c>\xfc</c>, <c>\U000000fc</c>), which the pattern above cannot see
    /// because the file holds the escape and not the letter. Under the tooling roots such a line is judged as the letter
    /// it spells: a name that has to contain the letter is written with the letter and carries the
    /// <c># legacy-name</c> marker, so there is one way to write it and the marker ceiling counts it.
    /// </summary>
    private static readonly Regex EscapedTurkishLetterPattern = new(
        @"\\u(?:00e7|011f|0131|00f6|015f|00fc|00c7|011e|0130|00d6|015e|00dc)"
        + @"|\\x(?:e7|f6|fc|c7|d6|dc)"
        + @"|\\U0000(?:00e7|011f|0131|00f6|015f|00fc|00c7|011e|0130|00d6|015e|00dc)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The twelve Turkish letters as escapes inside one character class. The tests of the tooling write it to prove that an
    /// output carries no Turkish letter, and it names no word, so it is the one escaped spelling the gate lets pass.
    /// </summary>
    private static readonly Regex EscapedLetterClassPattern = new(
        Regex.Escape(@"[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]"),
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Turkish words written without Turkish letters — the dominant form in this
    /// repository. Matching is whole-word and case-insensitive. A letter or digit of any script
    /// bounds a word, an underscore or hyphen does not: <c>test_gercek_ci</c> is judged word by word,
    /// while <c>naïve</c> is not split at the accented letter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Words that are also English words, or that appear inside identifiers, are
    /// deliberately absent: <c>her</c>, <c>son</c>, <c>alt</c>, <c>rol</c>,
    /// <c>ise</c>, <c>gibi</c>, <c>ama</c>, <c>para</c> (an XML doc tag),
    /// <c>once</c>, <c>var</c> and <c>tek</c>. The list below is wide enough that
    /// a Turkish sentence practically cannot avoid it.
    /// </para>
    /// <para>
    /// Two-letter words used to be excluded wholesale, which left a structural
    /// blind spot: a shipped validation message read
    /// <c>'{provider}:{model}' ne 'Input' ne 'Output'</c> and this gate could not
    /// see it, because <c>ne</c> carries no Turkish letter and no three-letter
    /// word appears in the phrase. The exclusion is now measured rather than
    /// assumed. Every two-letter connective and question particle this repository
    /// actually writes was counted across the scanned tree: <c>ne</c>, <c>ya</c>,
    /// <c>ki</c>, <c>mi</c>, <c>mu</c>, <c>da</c> and <c>ve</c> collide with
    /// nothing and are listed below. Three still collide and stay out —
    /// <c>de</c> and <c>en</c> are BCP-47 language tags that this repository
    /// passes around as literals, and <c>bu</c> appears in the end-to-end test
    /// that asserts translated Turkish UI text.
    /// </para>
    /// </remarks>
    private static readonly string[] TurkishWords =
    [
        // two-letter connectives and question particles — see the remarks above
        // for why these seven, and only these seven, are safe to list
        "ne", "ya", "ki", "mi", "mu", "da", "ve",
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
        "yok", "ses", "kullanilabilir", "daha",
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

    [Theory]
    [InlineData("docs-site/src/content/docs/api/Tracon.AgentDefinition.md", true)]
    [InlineData("docs-site/src/content/docs/http-api/agents.md", true)]
    [InlineData("docs-site/public/openapi/tracon.json", true)]
    [InlineData("docs-site/src/content/docs/guides/production.md", false)]
    [InlineData("docs-site/src/content/docs/apis.md", false)]
    [InlineData("docs-site/public/llms.txt", false)]
    public void Generated_site_trees_are_skipped_and_hand_written_pages_are_not(string path, bool skipped)
        => IsSkipped(path).ShouldBe(skipped);

    [Theory]
    [InlineData("See kayit-defteri.md for the reason", true)]
    [InlineData("python3 scripts/kayit-tara.py --check", false)]
    [InlineData("The reason is written in English", false)]
    [InlineData("def test_gercek_ci_yml_mutasyonlari(self):", true)]
    [InlineData("def test_scan_reports_a_sync_copy(self):", false)]
    [InlineData("public async Task Shell_still_loads_HATA_S4_003()", false)]
    [InlineData("var text = \"naïve café\";", false)]
    [InlineData("var text = \"bu yeni dosya icin yazildi\"; // legacy-name", true)]
    [InlineData("public void HATA_bu_yeni_dosya_icin_yazildi()", true)]
    public void A_line_is_offending_unless_only_a_masked_identifier_is_Turkish(string line, bool offending)
        => IsOffending(line).ShouldBe(offending);

    [Fact]
    public void The_scan_reaches_the_bench_the_site_scripts_the_tooling_and_text_files()
    {
        var root = Directory.CreateTempSubdirectory("tracon-source-language-").FullName;

        try
        {
            const string Turkish = "bu yeni dosya icin yazildi";

            foreach (var (path, text) in new[]
            {
                ("bench/capacity/Notes.md", Turkish),
                ("docs-site/scripts/check.mjs", $"// {Turkish}"),
                ("tests/Tracon.Package.Tests/Baselines/graph.txt", $"# {Turkish}"),
                ("scripts/tool.py", $"# {Turkish}"),
                (".github/workflows/ci.yml", $"# {Turkish}"),
                ("Directory.Build.props", $"<!-- {Turkish} -->"),
                (Path.Combine("docs", "notes.md"), Turkish),
                (Path.Combine("elsewhere", "tool", "tool.py"), $"# {Turkish}"),
                ("docs-site/src/content/docs/guides/clean.md", "Everything here is English."),
            })
            {
                var file = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }

            Scan(root).Keys.ShouldBe(
            [
                ".github/workflows/ci.yml",
                "Directory.Build.props",
                "bench/capacity/Notes.md",
                "docs-site/scripts/check.mjs",
                "scripts/tool.py",
                "tests/Tracon.Package.Tests/Baselines/graph.txt",
            ]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The root files the scan reads, written out here as well. A list that decides what the gate covers must not shrink
    /// because an entry was deleted or renamed in one place: removing a name from <c>ScannedRootFiles</c> fails this test
    /// until the name is removed here on purpose.
    /// </summary>
    private static readonly string[] PinnedRootFiles =
    [
        "README.md", "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md", "ARCHITECTURE.md", "LICENSE.md",
        ".editorconfig", ".gitattributes", ".gitignore", "Directory.Build.props", "Directory.Build.targets",
        "Directory.Packages.props", "NuGet.config", "global.json",
    ];

    [Fact]
    public void Every_pinned_root_file_and_every_scanned_extension_is_read_by_the_scan()
    {
        ScannedRootFiles.Order(StringComparer.Ordinal).ShouldBe(
            PinnedRootFiles.Order(StringComparer.Ordinal),
            "ScannedRootFiles and the names pinned in this test differ: a root file left the gate, or a new one joined it.");

        var root = Directory.CreateTempSubdirectory("tracon-source-language-roots-").FullName;

        try
        {
            const string Turkish = "bu yeni dosya icin yazildi";

            foreach (var name in PinnedRootFiles)
            {
                File.WriteAllText(
                    Path.Combine(root, name),
                    name.EndsWith(".json", StringComparison.Ordinal) ? $"{{\"note\": \"{Turkish}\"}}"
                    : name.EndsWith(".props", StringComparison.Ordinal) || name.EndsWith(".targets", StringComparison.Ordinal) || name.EndsWith(".config", StringComparison.Ordinal)
                        ? $"<!-- {Turkish} -->"
                    : name.EndsWith(".md", StringComparison.Ordinal) ? Turkish
                    : $"# {Turkish}");
            }

            // The two extensions the scan reads that no other test covers.
            Directory.CreateDirectory(Path.Combine(root, "scripts"));
            File.WriteAllText(Path.Combine(root, "scripts", "table.tsv"), $"# {Turkish}");
            File.WriteAllText(Path.Combine(root, "scripts", "run.sh"), $"# {Turkish}");

            string[] expected = [.. PinnedRootFiles, "scripts/run.sh", "scripts/table.tsv"];

            Scan(root).Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("old = \"--dosya\"  # legacy-name", "scripts/gate_test.py", false)]
    [InlineData("old = \"--dosya\"  # legacy-name", ".github/workflows/ci.yml", false)]
    [InlineData("old = \"--dosya\"  # legacy-name", "src/Tracon.Core/Thing.cs", true)]
    [InlineData("old = \"bu yeni dosya icin\" # legacy-name and more text", "scripts/gate_test.py", true)]
    public void The_legacy_name_marker_is_honoured_only_as_a_trailing_comment_under_the_tooling_roots(
        string line, string path, bool offending)
        => IsOffending(line, HonoursLegacyNameMarker(path)).ShouldBe(offending);

    [Theory]
    // The old class name spelled with escapes was the second way past the gate: the file holds the escape, not the letter.
    [InlineData("old = \"kan\\u0131ts\\u0131z\"", "scripts/public_surface_inventory_test.py", true)]
    [InlineData("old = \"kan\\U00000131ts\\U00000131z\"", ".github/workflows/ci.yml", true)]
    [InlineData("old = \"\\xe7ift\"", "scripts/gate_test.py", true)]
    [InlineData("old = \"kan\\u0131ts\\u0131z\"  # legacy-name", "scripts/public_surface_inventory_test.py", false)]
    // The class of the twelve letters is how a test proves that an output has none.
    [InlineData("re.search(\"[\\u00e7\\u011f\\u0131\\u00f6\\u015f\\u00fc\\u00c7\\u011e\\u0130\\u00d6\\u015e\\u00dc]\", text)", "scripts/capacity_test.py", false)]
    [InlineData("re.search(\"[\\u00e7\\u0131]\", text)", "scripts/capacity_test.py", true)]
    // Outside the tooling roots an escape is how a test spells a Turkish letter on purpose; the baseline counts letters, not escapes.
    [InlineData("var folded = \"kan\\u0131ts\\u0131z\";", "tests/Tracon.Core.UnitTests/Thing.cs", false)]
    public void An_escaped_Turkish_letter_under_the_tooling_roots_is_judged_as_the_letter_it_spells(
        string line, string path, bool offending)
        => IsOffending(line, HonoursLegacyNameMarker(path)).ShouldBe(offending);

    [Fact]
    public void A_workflow_pinned_to_another_repository_is_skipped_and_one_pinned_here_is_scanned()
    {
        var root = Directory.CreateTempSubdirectory("tracon-source-language-").FullName;

        try
        {
            const string Turkish = "bu yeni dosya icin yazildi";
            var workflows = Directory.CreateDirectory(Path.Combine(root, ".github", "workflows")).FullName;

            File.WriteAllText(Path.Combine(workflows, "elsewhere.yml"), $"# {Turkish}\njobs:\n  one:\n    if: github.repository == 'someone/Other'\n");
            File.WriteAllText(Path.Combine(workflows, "here.yml"), $"# {Turkish}\njobs:\n  one:\n    if: github.repository == '{CanonicalRepository}'\n");
            File.WriteAllText(Path.Combine(workflows, "mixed.yml"), $"# {Turkish}\njobs:\n  one:\n    if: github.repository == 'someone/Other'\n  two:\n    if: github.repository == '{CanonicalRepository}'\n");
            File.WriteAllText(Path.Combine(workflows, "unpinned.yml"), $"# {Turkish}\njobs:\n  one:\n    runs-on: ubuntu-latest\n");

            Scan(root).Keys.ShouldBe(
            [
                ".github/workflows/here.yml",
                ".github/workflows/mixed.yml",
                ".github/workflows/unpinned.yml",
            ]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_legacy_name_marker_stays_under_its_ceiling()
    {
        var used = 0;

        foreach (var root in LegacyNameRoots)
        {
            var absoluteRoot = Path.Combine(RepositoryRoot, root);

            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');

                if (!ScannedExtensions.Contains(Path.GetExtension(file)) || IsSkipped(relative))
                {
                    continue;
                }

                used += File.ReadLines(file).Count(line => LegacyNameMarkerPattern.IsMatch(line));
            }
        }

        used.ShouldBeLessThanOrEqualTo(
            LegacyNameLineCeiling,
            $"{used} lines carry the legacy-name marker; the ceiling is {LegacyNameLineCeiling} and it can only shrink.");
    }

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
                           "dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    private static SortedDictionary<string, int> Scan() => Scan(RepositoryRoot);

    private static SortedDictionary<string, int> Scan(string repositoryRoot)
    {
        var results = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var root in ScanRoots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root);

            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');

                if (!ScannedExtensions.Contains(Path.GetExtension(file)) &&
                    !PackagedReadmePattern.IsMatch(relative))
                {
                    continue;
                }

                if (IsSkipped(relative, repositoryRoot))
                {
                    continue;
                }

                var count = CountOffendingLines(file, relative);

                if (count > 0)
                {
                    results[relative] = count;
                }
            }
        }

        foreach (var name in ScannedRootFiles)
        {
            var file = Path.Combine(repositoryRoot, name);

            if (!File.Exists(file))
            {
                continue;
            }

            var count = CountOffendingLines(file, name);

            if (count > 0)
            {
                results[name] = count;
            }
        }

        return results;
    }

    /// <summary>The repository a workflow job names in its <c>if:</c> condition.</summary>
    private static readonly Regex RepositoryPinPattern = new(
        @"github\.repository\s*==\s*'(?<repository>[^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>The repository the shipped tree lives in; a workflow pinned to any other one is not a workflow of this tree.</summary>
    private const string CanonicalRepository = "farukatasoy/Tracon";

    /// <summary>
    /// Whether a workflow file runs only in a repository other than this one. Such a workflow never runs here, and the tree
    /// that is published does not receive it: it belongs to the repository its jobs are pinned to, and it may call that
    /// repository's own tooling, which is not written in English. The rule reads the pin the file carries, so no file
    /// name of a private workflow is written in a test that ships.
    /// </summary>
    private static bool IsPinnedToAnotherRepository(string repositoryRoot, string relativePath)
    {
        if (!relativePath.StartsWith(".github/workflows/", StringComparison.Ordinal)
            || !relativePath.EndsWith(".yml", StringComparison.Ordinal))
        {
            return false;
        }

        var path = Path.Combine(repositoryRoot, relativePath);

        if (!File.Exists(path))
        {
            return false;
        }

        var pins = RepositoryPinPattern.Matches(File.ReadAllText(path)).Select(static match => match.Groups["repository"].Value).ToList();

        return pins.Count > 0 && pins.All(static pin => !string.Equals(pin, CanonicalRepository, StringComparison.Ordinal));
    }

    private static bool IsSkipped(string relativePath, string? repositoryRoot = null)
    {
        if (SkippedFiles.Contains(relativePath, StringComparer.Ordinal))
        {
            return true;
        }

        if (IsPinnedToAnotherRepository(repositoryRoot ?? RepositoryRoot, relativePath))
        {
            return true;
        }

        foreach (var prefix in GeneratedSitePrefixes)
        {
            if (relativePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
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

    private static int CountOffendingLines(string file, string relativePath)
    {
        var count = 0;
        var markerHonoured = HonoursLegacyNameMarker(relativePath);

        foreach (var line in File.ReadLines(file))
        {
            if (IsOffending(line, markerHonoured))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HonoursLegacyNameMarker(string relativePath)
        => LegacyNameRoots.Any(root => relativePath.StartsWith(root, StringComparison.Ordinal));

    private static bool IsOffending(string line, bool underToolingRoots = false)
    {
        if (line.Length == 0 || AllowedLinePattern.IsMatch(line))
        {
            return false;
        }

        if (underToolingRoots && LegacyNameMarkerPattern.IsMatch(line))
        {
            return false;
        }

        var inspected = IdentifierMaskPattern.Replace(line, string.Empty);

        if (TurkishLetterPattern.IsMatch(inspected) || TurkishWordPattern.IsMatch(inspected))
        {
            return true;
        }

        return underToolingRoots
            && EscapedTurkishLetterPattern.IsMatch(EscapedLetterClassPattern.Replace(inspected, string.Empty));
    }

    private static Regex BuildWordPattern()
    {
        var alternatives = TurkishWords
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(word => word.Length)
            .ThenBy(word => word, StringComparer.Ordinal);

        return new Regex(
            $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}])",
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
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
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
        "Tracon.Core.UnitTests",
        "Architecture",
        "source-language-baseline.txt");

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
