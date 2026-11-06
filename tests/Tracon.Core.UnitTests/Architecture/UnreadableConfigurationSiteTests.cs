using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// No configuration binder reads a scalar with the skip-and-keep-the-default pattern: every
/// binder file reads through <c>ConfigurationValueReader</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>if (int.TryParse(section[key], out var v)) { options.X = v; }</c> leaves the shipped
/// default in place for text it cannot read. 181 sites read that way and 25 of their
/// defaults are the loose side - a typo opened a limit or switched a protection off. The
/// reader records such text and <c>UnreadableConfigurationValidator</c> stops the host at
/// start.
/// </para>
/// <para>
/// This scan keeps a new binder from bringing the pattern back. It reads the binder files
/// only - a <c>TryParse</c> of a header, a cursor or a command-line value is legitimate
/// elsewhere - and no site is exempt: the reader itself lives in <c>Configuration/</c>,
/// which is not a binder file. A binder file is found by name; the second test proves
/// that every file that uses the reader is one of them, so a binder in a file the name
/// pattern misses cannot hide.
/// </para>
/// <para>
/// The framework's own binder is the other way to read a section, and it is not allowed in a binder file
/// either. It reads <c>30</c> for a duration as 30 days, throws a raw exception for text it cannot read, and
/// registers a change token source that makes a reload reach some readers of the options and not others
/// (the workflow options were the one case, until they were bound by hand).
/// </para>
/// </remarks>
public sealed partial class UnreadableConfigurationSiteTests
{
    [Fact]
    public void No_binder_file_reads_a_scalar_with_the_skip_and_keep_pattern()
    {
        var root = FindRepositoryRoot();
        var files = BinderFiles(root).ToList();

        // A scan that found few files would pass for the wrong reason.
        files.Count.ShouldBeGreaterThan(15);

        var offending = new List<string>();

        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);

            for (var index = 0; index < lines.Length; index++)
            {
                if (SilentRead().IsMatch(lines[index]) && !IsComment(lines[index]))
                {
                    offending.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        offending.ShouldBeEmpty(
            "A binder reads a scalar with TryParse (or a helper that wraps it) and keeps the default for text it cannot " +
            "read. Read it with ConfigurationValueReader instead (Int32, Boolean, TimeSpan, Enum, ...), so the host " +
            "stops at start and names the key.");
    }

    [Fact]
    public void Every_file_that_uses_the_reader_is_a_binder_file()
    {
        var root = FindRepositoryRoot();
        var binderFiles = BinderFiles(root).Select(path => Path.GetFullPath(path)).ToHashSet(StringComparer.Ordinal);

        var users = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}Configuration{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static path => ReaderUse().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetFullPath(path))
            .ToList();

        users.Count.ShouldBeGreaterThan(15);

        users.Where(path => !binderFiles.Contains(path))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ShouldBeEmpty("A file reads configuration through the reader but is not scanned: extend BinderFiles().");
    }

    [Fact]
    public void No_binder_file_reads_a_section_with_the_framework_binder()
    {
        var root = FindRepositoryRoot();
        var offending = new List<string>();

        foreach (var path in BinderFiles(root))
        {
            var lines = File.ReadAllLines(path);

            for (var index = 0; index < lines.Length; index++)
            {
                if (FrameworkBinder().IsMatch(lines[index]) && !IsComment(lines[index]))
                {
                    offending.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        // BindConfiguration is refused in every file of src: a registration that is not in a file the name pattern finds
        // would bring back the three defects as well.
        foreach (var path in Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path)))
        {
            var lines = File.ReadAllLines(path);

            for (var index = 0; index < lines.Length; index++)
            {
                if (BindConfigurationCall().IsMatch(lines[index]) && !IsComment(lines[index]))
                {
                    offending.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        offending.Distinct(StringComparer.Ordinal).ShouldBeEmpty(
            "A source file reads a section with the framework's configuration binder (BindConfiguration, Bind, Get<T>). " +
            "Read it by hand with ConfigurationValueReader, so a duration is a clock time, text it cannot read stops the " +
            "host and names the key, and no change token source is registered.");
    }

    [Theory]
    [InlineData("            .BindConfiguration(TraconWorkflowOptions.SectionName)")]
    [InlineData("services.AddOptions<X>().BindConfiguration(\"Tracon:X\");")]
    [InlineData("section.Bind(options);")]
    [InlineData("configuration.GetSection(\"Tracon\").Bind(options);")]
    [InlineData("optionsBuilder.Bind(section);")]
    [InlineData("var options = section.Get<MyOptions>();")]
    [InlineData("var options = configuration.GetSection(\"Tracon:X\").Get<MyOptions>();")]
    [InlineData("            .Bind(section);")]
    [InlineData("builder.Configuration.Bind(options);")]
    [InlineData("ConfigurationBinder.Bind(section, options);")]
    [InlineData("var value = ConfigurationBinder.Get<MyOptions>(section);")]
    public void The_framework_binder_pattern_sees_the_framework_reads(string source)
        => FrameworkBinder().IsMatch(source).ShouldBeTrue();

    [Theory]
    [InlineData("return builder.UseOpenAICompatible(name, options => OpenAIProviderExtensions.Bind(configurationSection, options));")]
    [InlineData("Bind(configuration.GetSection(TraconWorkflowOptions.SectionName), options);")]
    [InlineData("var boundInstructions = InstructionParameterBinder.Bind(")]
    [InlineData("response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();")]
    [InlineData("read.Int32(nameof(X.Y), static o => o.Y, static (o, v) => o.Y = v);")]
    public void The_framework_binder_pattern_leaves_the_hand_written_binders_alone(string source)
        => FrameworkBinder().IsMatch(source).ShouldBeFalse();

    [Theory]
    [InlineData("if (int.TryParse(section[key], out var v))")]
    [InlineData("if (TimeSpan.TryParse(section[nameof(X.Y)], CultureInfo.InvariantCulture, out var v))")]
    [InlineData("if (TryReadBool(section, nameof(X.Y), out var enabled))")]
    [InlineData("if (TryInt(section[\"A\"], out var a))")]
    [InlineData("if (TryTimeSpan(section[\"A\"], out var a))")]
    [InlineData("var pii = Enum.TryParse<PiiPatterns>(text, ignoreCase: true, out var parsed)")]
    [InlineData("if (ProviderRegistrationCore.ReadInt32(section, key) is { } v)")]
    [InlineData("? decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)")]
    [InlineData("&& Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed))")]
    public void The_pattern_sees_the_silent_reads(string source)
        => SilentRead().IsMatch(source).ShouldBeTrue();

    [Theory]
    [InlineData("read.Int32(nameof(X.Y), static o => o.Y, static (o, v) => o.Y = v);")]
    [InlineData("if (Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out var uri))")]
    [InlineData("if (section[nameof(X.Name)] is { Length: > 0 } name)")]
    public void The_pattern_leaves_the_reader_and_other_reads_alone(string source)
        => SilentRead().IsMatch(source).ShouldBeFalse();

    private static IEnumerable<string> BinderFiles(string root)
        => Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .Where(static path => BinderFileName().IsMatch(Path.GetFileName(path)));

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    // The files that bind configuration by hand: the builder and provider extension files, the
    // TraconServiceCollectionExtensions partials (Binding.* and Registration.*), the shared provider registration.
    [GeneratedRegex(
        @"(?:BuilderExtensions|ProviderExtensions)[\w.]*\.cs$|^TraconServiceCollectionExtensions[\w.]*\.cs$|^ProviderRegistrationCore\.cs$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex BinderFileName();

    // A typed TryParse, the repository's own wrappers around it, an enum TryParse, the old shared
    // readers of ProviderRegistrationCore, and an absolute-only Uri.TryCreate: it kept the default for
    // a relative or mistyped address. RelativeOrAbsolute is left alone - the options validator
    // refuses the relative address it lets through (ProviderRegistrationCore.ReadEndpoint).
    [GeneratedRegex(
        @"\b(?:int|long|double|float|decimal|bool|TimeSpan)\.TryParse\(|\bEnum\.TryParse\b|\bTryReadBool\(|\bTryInt\(|\bTryTimeSpan\(|ProviderRegistrationCore\.Read(?:Int32|TimeSpan|Boolean|Decimal)\(|\bUri\.TryCreate\([^)]*\bUriKind\.Absolute\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex SilentRead();

    [GeneratedRegex(@"\bBindConfiguration\s*[(<]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex BindConfigurationCall();

    // The framework binder: BindConfiguration, an instance's Bind (a lower-case receiver, a call chain or a line that
    // starts with the dot, so the repository's own static Bind(section, options) methods are left alone), ConfigurationBinder
    // itself, and Get<T> on a configuration.
    [GeneratedRegex(
        @"\bBindConfiguration\b|(?:\b[a-z]\w*|\))\s*\.Bind\(|^\s*\.Bind\(|\bConfigurationBinder\.|\b(?:Configuration|configuration|configurationSection|section|[a-z]\w*Section)\s*\.(?:Bind\(|Get<)|\)\s*\.Get<[A-Z]\w*>\(\)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex FrameworkBinder();

    [GeneratedRegex(
        @"\bConfigurationValueReader\.For\(|\bnew\s+ConfigurationValueBatch\(",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex ReaderUse();

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
