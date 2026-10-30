using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Configuration;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins the configuration keys Tracon reads to <c>docs/sozlesme/yapilandirma.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// A configuration key is a name a consumer writes in <c>appsettings.json</c> or an environment variable, so
/// removing or renaming one is a breaking change in 1.x, and so is changing what a key means. A new key is not.
/// </para>
/// <para>
/// Where the keys come from. The options types are bound by hand (no reflection in the library), so
/// there is no schema to read. The public surface of every tracked package is: a <c>PublicAPI.*.txt</c> file
/// lists every option type with its properties and their types. The tree is walked from every type that names a
/// section (<c>const SectionName</c>) and from the two sections whose options type does not
/// (<see cref="RootsWithoutSectionName"/>). A dictionary or a list contributes <c>*</c> for its key. Reading
/// the text rather than the assemblies keeps this test in <c>Tracon.Core.UnitTests</c>: the provider, voice,
/// workflow and database packages are not referenced from here, and referencing them would pull their SDKs in.
/// </para>
/// <para>
/// What the tree must NOT contain. A property that no key reaches is not a key. Three members of the skill
/// script options are closed on purpose (<see cref="NotBoundByDesign"/>), and
/// <see cref="Keys_closed_on_purpose_stay_unbound"/> proves they stay closed. That the rest are bound is the
/// job of <c>TraconOptionsBindingCoverageTests</c> and the <c>{Type}BindingTests</c> classes.
/// </para>
/// </remarks>
public sealed class ConfigurationKeySnapshotTests
{
    /// <summary>Sections bound to an options type that has no <c>SectionName</c> constant.</summary>
    private static readonly (string Path, string Type)[] RootsWithoutSectionName =
    [
        ("Tracon:TenantProviders", "Tracon.TraconTenantProviderOptions"),
        ("Tracon:InboundTriggers", "Tracon.TraconInboundTriggerOptions"),
    ];

    /// <summary>
    /// Keys a consumer writes that are read straight from <c>IConfiguration</c> and bound to no options type, so the
    /// public API text cannot list them. The audit chain key VALUES are the case: they are a <c>secret</c> and
    /// are not bound to an options object, so nothing that prints an options object can print one.
    /// </summary>
    private static readonly string[] ReadWithoutAnOptionsType =
    [
        "Tracon:AuditChainKeys:*",
    ];

    /// <summary>
    /// Members that are options on the type and not keys in configuration. The shipped documentation promises that
    /// script execution "can only be turned on in code": binding these from configuration would let an
    /// environment variable widen what the server may execute (the binder says so beside the code that leaves
    /// them out). <c>EnvironmentAllowList</c> is NOT here: it is bound, and the key is documented.
    /// </summary>
    private static readonly string[] NotBoundByDesign =
    [
        "Tracon:Skills:Scripts:AllowStoredScripts",
        "Tracon:Skills:Scripts:Interpreters:*",
        "Tracon:Skills:Scripts:SkillRoots:*",
    ];

    /// <summary>
    /// <c>Tracon:UtilityModel</c> is a <c>ModelBinding</c>, which has nineteen members, but its binder reads six
    /// (<c>BindUtilityModel</c>). The rest, eighteen keys with the ones under <c>ToolLoop</c>, <c>ResponseCache</c> and
    /// <c>ResponseFormat</c>, are options on the type and not keys: a consumer who writes
    /// <c>Tracon:UtilityModel:Seed</c> gets nothing, and the snapshot must not promise them for the whole 1.x line.
    /// <see cref="Utility_model_keys_the_binder_ignores_stay_out_of_the_snapshot"/> proves it with the container.
    /// </summary>
    private static readonly string[] UtilityModelBoundKeys =
    [
        "Tracon:UtilityModel:MaxOutputTokens",
        "Tracon:UtilityModel:Model",
        "Tracon:UtilityModel:Provider",
        "Tracon:UtilityModel:ReasoningEffort",
        "Tracon:UtilityModel:Temperature",
        "Tracon:UtilityModel:TopP",
    ];

    private static readonly HashSet<string> ScalarTypes = new(StringComparer.Ordinal)
    {
        "bool", "byte", "short", "int", "long", "float", "double", "decimal", "string", "char",
        "System.TimeSpan", "System.DateTimeOffset", "System.DateOnly", "System.Uri", "System.Guid",
    };

    private static readonly Regex TypeLine = new(
        @"^[A-Za-z_][\w.]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex GetterLine = new(
        @"^(?<type>[\w.]+)\.(?<member>\w+)\.get -> (?<result>.+)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex SetterLine = new(
        @"^(?<type>[\w.]+)\.(?<member>\w+)\.(?:set|init) -> void$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex SectionConstant = new(
        """^const (?<type>[\w.]+)\.SectionName = "(?<path>[^"]+)" -> string!$""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex EnumMemberLine = new(
        @"^(?<type>[\w.]+)\.\w+ = -?\d+ -> \k<type>$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex CollectionType = new(
        @"^System\.Collections\.Generic\.(?<kind>IList|List|IReadOnlyList|IReadOnlyCollection|ICollection|IEnumerable|HashSet|ISet|IReadOnlySet|IDictionary|Dictionary|IReadOnlyDictionary)<(?<arguments>.+)>$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    // Registration code hands a section to its binder as `configurationSection.GetSection("Quotas")`; the name is
    // relative to the `Tracon` section. `GetSection("Tracon:Voice")` is absolute.
    private static readonly Regex RelativeSection = new(
        """configurationSection\.GetSection\("(?<name>\w+)"\)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AbsoluteSection = new(
        """GetSection\("(?<name>Tracon:[\w:]+)"\)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void The_snapshot_matches_the_keys_in_the_code()
    {
        ContractSnapshot.Verify(
            "docs/sozlesme/yapilandirma.txt",
            "tests/Tracon.Core.UnitTests/Architecture/ConfigurationKeySnapshotTests.cs",
            "The configuration keys Tracon reads, '*' standing for a name the consumer chooses",
            Keys());
    }

    [Fact]
    public void The_key_tree_finds_the_shape_it_is_known_to_find()
    {
        var keys = Keys().ToHashSet(StringComparer.Ordinal);

        // 2026-10-24: 355 keys. A walk that finds nothing would pass the snapshot test for the wrong reason.
        keys.Count.ShouldBeGreaterThan(250);
        keys.Contains("Tracon:Observability:IncludeAgentVersionTag").ShouldBeTrue("Tracon:Observability:IncludeAgentVersionTag is missing from the key tree");
        keys.Contains("Tracon:Observability:IncludeTenantTag").ShouldBeTrue("Tracon:Observability:IncludeTenantTag is missing from the key tree");
        keys.Contains("Tracon:Quotas:ThresholdPercents:*").ShouldBeTrue("Tracon:Quotas:ThresholdPercents:* is missing from the key tree");
        keys.Contains("Tracon:Pricing:Providers:*:*:InputCostPerMillionTokens").ShouldBeTrue("Tracon:Pricing:Providers:*:*:InputCostPerMillionTokens is missing from the key tree");
        keys.Contains("Tracon:Providers:OpenAI:Models:*:ContextWindowTokens").ShouldBeTrue("Tracon:Providers:OpenAI:Models:*:ContextWindowTokens is missing from the key tree");
        keys.Contains("Tracon:Skills:Scripts:EnvironmentAllowList:*").ShouldBeTrue("Tracon:Skills:Scripts:EnvironmentAllowList:* is bound and must be in the key tree");
        keys.Contains("Tracon:SqlServer:ConnectionString").ShouldBeTrue("Tracon:SqlServer:ConnectionString is missing from the key tree");
        keys.Contains("Tracon:TenantProviders:AllowedConfigurationPrefix").ShouldBeTrue("Tracon:TenantProviders:AllowedConfigurationPrefix is missing from the key tree");
        keys.Contains("Tracon:Voice:Conversation:IdleTimeout").ShouldBeTrue("Tracon:Voice:Conversation:IdleTimeout is missing from the key tree");
        keys.ShouldAllBe(static key => key.StartsWith("Tracon", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_section_the_registration_binds_has_keys_in_the_tree()
    {
        var keys = Keys();
        var sections = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            foreach (Match match in RelativeSection.Matches(file.Text))
            {
                sections.Add($"Tracon:{match.Groups["name"].Value}");
            }

            foreach (Match match in AbsoluteSection.Matches(file.Text))
            {
                sections.Add(match.Groups["name"].Value);
            }
        }

        // The scan found the sections the registration code hands to a binder; 2026-10-24: 29 relative, 3 absolute.
        sections.Count.ShouldBeGreaterThan(25);

        var withoutKeys = sections
            .Where(section => !keys.Any(key => key.StartsWith(section + ":", StringComparison.Ordinal)))
            .ToList();

        withoutKeys.ShouldBeEmpty(
            "A section is bound by hand but no option type with a SectionName (or an entry in RootsWithoutSectionName) " +
            "reaches it, so its keys are missing from the snapshot.");
    }

    [Fact]
    public void Keys_closed_on_purpose_stay_unbound()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Skills:Scripts:AllowStoredScripts"] = "true",
                ["Tracon:Skills:Scripts:Interpreters:.probe"] = "probe-interpreter",
                ["Tracon:Skills:Scripts:SkillRoots:0"] = "/probe/roots",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection(TraconOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var scripts = provider.GetRequiredService<IOptions<TraconOptions>>().Value.Skills.Scripts;
        var shipped = new TraconOptions().Skills.Scripts;

        scripts.AllowStoredScripts.ShouldBe(shipped.AllowStoredScripts);
        scripts.SkillRoots.ShouldBe(shipped.SkillRoots);
        scripts.Interpreters.ShouldBe(shipped.Interpreters);

        foreach (var closed in NotBoundByDesign)
        {
            Keys().Contains(closed, StringComparer.Ordinal).ShouldBeFalse($"{closed} is closed on purpose and is in the snapshot.");
        }
    }

    [Fact]
    public void Utility_model_keys_the_binder_ignores_stay_out_of_the_snapshot()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:UtilityModel:Provider"] = "openai",
            ["Tracon:UtilityModel:Model"] = "gpt-5.4-mini",
            ["Tracon:UtilityModel:Seed"] = "7",
            ["Tracon:UtilityModel:TopK"] = "3",
            ["Tracon:UtilityModel:FrequencyPenalty"] = "0.5",
            ["Tracon:UtilityModel:PresencePenalty"] = "0.5",
            ["Tracon:UtilityModel:AllowMultipleToolCalls"] = "true",
            ["Tracon:UtilityModel:ToolMode"] = "none",
        };

        var binding = SectionBinding.Bind<TraconOptions>(values).UtilityModel.ShouldNotBeNull();

        // If one of these starts to bind, it is a key now: add it to UtilityModelBoundKeys and refresh the snapshot.
        binding.Seed.ShouldBeNull();
        binding.TopK.ShouldBeNull();
        binding.FrequencyPenalty.ShouldBeNull();
        binding.PresencePenalty.ShouldBeNull();
        binding.AllowMultipleToolCalls.ShouldBeNull();
        binding.ToolMode.ShouldBeNull();

        Keys().Where(static key => key.StartsWith("Tracon:UtilityModel:", StringComparison.Ordinal))
            .ShouldBe(UtilityModelBoundKeys, ignoreOrder: true);
    }

    [Fact]
    public void Generic_arguments_split_at_the_top_level_comma()
    {
        SplitArguments("string!, System.Collections.Generic.IDictionary<string!, Tracon.ModelPriceOverride!>!")
            .ShouldBe(["string!", "System.Collections.Generic.IDictionary<string!, Tracon.ModelPriceOverride!>!"]);
        SplitArguments("string!").ShouldBe(["string!"]);
    }

    private static List<string> Keys()
    {
        var api = ReadApi();
        var roots = new List<(string Path, string Type)>(api.Sections);
        roots.AddRange(RootsWithoutSectionName);

        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, type) in roots)
        {
            Walk(api, type, path, keys, ImmutableHashSet<string>.Empty);
        }

        keys.UnionWith(ReadWithoutAnOptionsType);
        keys.ExceptWith(NotBoundByDesign);
        keys.RemoveWhere(static key => key.StartsWith("Tracon:UtilityModel:", StringComparison.Ordinal)
            && !UtilityModelBoundKeys.Contains(key, StringComparer.Ordinal));

        return [.. keys];
    }

    private static void Walk(Api api, string type, string path, SortedSet<string> keys, ImmutableHashSet<string> visiting)
    {
        // A type that contains itself would never end; the options tree has none, the guard keeps it that way.
        if (visiting.Contains(type) || !api.Properties.TryGetValue(type, out var members))
        {
            return;
        }

        foreach (var (name, member) in members)
        {
            Emit(api, member.Type, $"{path}:{name}", member.Settable, keys, visiting.Add(type));
        }
    }

    private static void Emit(Api api, string rawType, string key, bool settable, SortedSet<string> keys, ImmutableHashSet<string> visiting)
    {
        var type = rawType.Replace("!", string.Empty, StringComparison.Ordinal).Replace("?", string.Empty, StringComparison.Ordinal);

        if (ScalarTypes.Contains(type) || api.Enums.Contains(type))
        {
            if (settable)
            {
                keys.Add(key);
            }

            return;
        }

        if (CollectionType.Match(type) is { Success: true } collection)
        {
            var arguments = SplitArguments(collection.Groups["arguments"].Value);
            var isDictionary = collection.Groups["kind"].Value.Contains("Dictionary", StringComparison.Ordinal);
            var element = arguments[^1];

            // A name or an index the consumer chooses: `*`. What it holds is a scalar (the key ends here) or an
            // options object (its members follow) or another collection.
            var elementKey = $"{key}:*";

            if (isDictionary || arguments.Count == 1)
            {
                var plain = element.Replace("!", string.Empty, StringComparison.Ordinal).Replace("?", string.Empty, StringComparison.Ordinal);

                if (ScalarTypes.Contains(plain) || api.Enums.Contains(plain))
                {
                    keys.Add(elementKey);
                }
                else
                {
                    Emit(api, element, elementKey, settable: true, keys, visiting);
                }
            }

            return;
        }

        if (api.Properties.ContainsKey(type) && api.Types.Contains(type))
        {
            Walk(api, type, key, keys, visiting);
        }

        // Anything else (a delegate, a data source, a JSON element) cannot be written as text: not a key.
    }

    private static List<string> SplitArguments(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        parts.Add(text[start..].Trim());

        return parts;
    }

    private static Api ReadApi()
    {
        var sections = new List<(string Path, string Type)>();
        var properties = new Dictionary<string, SortedDictionary<string, Member>>(StringComparer.Ordinal);
        var types = new HashSet<string>(StringComparer.Ordinal);
        var enums = new HashSet<string>(StringComparer.Ordinal);

        foreach (var package in Directory.EnumerateDirectories(Path.Combine(RepoRoot.Path, "src")).Order(StringComparer.Ordinal))
        {
            var unshipped = Path.Combine(package, "PublicAPI.Unshipped.txt");

            if (!File.Exists(unshipped))
            {
                continue;
            }

            foreach (var line in EffectiveLines(Path.Combine(package, "PublicAPI.Shipped.txt"), unshipped))
            {
                if (SectionConstant.Match(line) is { Success: true } section)
                {
                    sections.Add((section.Groups["path"].Value, section.Groups["type"].Value));
                }
                else if (GetterLine.Match(line) is { Success: true } getter)
                {
                    GetMember(properties, getter.Groups["type"].Value, getter.Groups["member"].Value).Type = getter.Groups["result"].Value;
                }
                else if (SetterLine.Match(line) is { Success: true } setter)
                {
                    GetMember(properties, setter.Groups["type"].Value, setter.Groups["member"].Value).Settable = true;
                }
                else if (EnumMemberLine.Match(line) is { Success: true } enumMember)
                {
                    enums.Add(enumMember.Groups["type"].Value);
                }
                else if (TypeLine.IsMatch(line))
                {
                    types.Add(line);
                }
            }
        }

        // A setter line without a getter line cannot be a property of a type the walk reads; drop the empty ones.
        foreach (var members in properties.Values)
        {
            foreach (var name in members.Where(static pair => pair.Value.Type.Length == 0).Select(static pair => pair.Key).ToList())
            {
                members.Remove(name);
            }
        }

        return new Api(sections, properties.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), types, enums);
    }

    private static Member GetMember(Dictionary<string, SortedDictionary<string, Member>> properties, string type, string name)
    {
        if (!properties.TryGetValue(type, out var members))
        {
            properties[type] = members = new SortedDictionary<string, Member>(StringComparer.Ordinal);
        }

        if (!members.TryGetValue(name, out var member))
        {
            members[name] = member = new Member();
        }

        return member;
    }

    /// <summary>The surface a package declares: the shipped lines and the unshipped ones, without the removed ones.</summary>
    private static HashSet<string> EffectiveLines(string shippedPath, string unshippedPath)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, isUnshipped) in new[] { (shippedPath, false), (unshippedPath, true) })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim().TrimStart('﻿');

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (isUnshipped && line.StartsWith("*REMOVED*", StringComparison.Ordinal))
                {
                    removed.Add(line["*REMOVED*".Length..].Trim());
                }
                else
                {
                    lines.Add(line);
                }
            }
        }

        lines.ExceptWith(removed);

        return lines;
    }

    private static IEnumerable<(string Path, string Text)> SourceFiles()
    {
        var src = Path.Combine(RepoRoot.Path, "src");

        foreach (var path in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoRoot.Path, path).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (relative, File.ReadAllText(path));
        }
    }

    private sealed class Member
    {
        public string Type { get; set; } = string.Empty;

        public bool Settable { get; set; }
    }

    private sealed record Api(
        List<(string Path, string Type)> Sections,
        Dictionary<string, SortedDictionary<string, Member>> Properties,
        HashSet<string> Types,
        HashSet<string> Enums);
}
