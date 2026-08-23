using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Configuration;

/// <summary>
/// <c>AgentPrismOptions</c> is bound by hand (K-021, AOT). This structurally locks
/// out the "new field added but not added to <c>Bind()</c>" defect — the same
/// defect was found independently three times: <c>RunRecording.RecordRunInput</c>
/// (K-406), <c>Observability.IncludeAgentVersionTag</c> (MT-OBS-036), and
/// <c>Validation.McpTimeout</c> (K-253 — defined but never bound).
/// </summary>
/// <remarks>
/// This test fills EVERY scalar (bool/int/long/double/decimal/string/TimeSpan/enum)
/// field in the <c>AgentPrismOptions</c> tree with a single configuration set and
/// reads it back, serving as durable proof that <c>Bind()</c> really processes
/// every field — instead of adding individual regression tests, this test breaks
/// if a future field is forgotten in Bind().
/// </remarks>
public sealed class AgentPrismOptionsBindingCoverageTests
{
    /// <summary>
    /// Dictionary/collection-typed fields (<c>Providers</c>, <c>Voice</c>,
    /// <c>AllowedMediaTypes</c>, <c>SkillRoots</c>, <c>Interpreters</c>,
    /// <c>EnvironmentAllowList</c>) and the custom-conversion <c>Pricing</c>/
    /// <c>UtilityModel</c> fields are already covered by their own independent
    /// tests (BindPricing/BindUtilityModel scenarios, family O provider tests);
    /// this generic scanner only verifies "flat" (property name = config key) binding.
    /// </summary>
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.Ordinal)
    {
        "Pricing",
        "UtilityModel",
        "Skills.Scripts.SkillRoots",
        "Skills.Scripts.Interpreters",

        // 🚨 NOT bound on purpose, and this is a security boundary rather than a
        // gap in the scanner. AllowStoredScripts widens what may be executed on
        // the server, and the shipped documentation promises that script
        // execution "can only be turned on in code". Binding merged INTO the
        // code-supplied values instead of replacing them, so configuration could
        // only ever ADD to the executable surface - an environment variable was
        // enough to open the stored-script path the application had deliberately
        // left closed. SkillRoots and Interpreters above are closed for the same
        // reason, not merely because they are collections.
        //
        // Enabled and PlatformIsolationAcknowledged stay bound: they cannot widen
        // anything, they only switch the feature off or acknowledge the platform
        // limits.
        "Skills.Scripts.AllowStoredScripts",
        "Skills.Scripts.EnvironmentAllowList",
        "Attachments.AllowedMediaTypes",

        // The generic int sentinel is "(current ?? 0) + 7"; this field's own
        // AgentPrismOptionsValidator floor is TruncatingAIFunction.MinimumEnvelopeBytes
        // (57 at time of writing) — the generic +7 sentinel would fail that
        // check before the scan ever reads the bound value back. Covered
        // directly by DefaultMaxOutputBytes_binds_from_configuration below.
        "Tools.DefaultMaxOutputBytes",
    };

    [Fact]
    public void Every_scalar_field_in_AgentPrismOptions_tree_is_bound_from_configuration()
    {
        var configValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        var expectations = new List<(string Path, PropertyInfo Property, object Expected)>();

        Collect(new AgentPrismOptions(), AgentPrismOptions.SectionName, [], configValues, expectations);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var services = new ServiceCollection();
        services.AddAgentPrism(configuration.GetSection(AgentPrismOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var bound = provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value;

        var failures = new List<string>();

        foreach (var (path, property, expected) in expectations)
        {
            var actual = ResolveValue(bound, path, property);

            if (!Equals(actual, expected))
            {
                failures.Add($"'{path}': expected '{expected}', got '{actual}' — may have been forgotten in Bind().");
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join('\n', failures));
    }

    /// <summary>
    /// Excluded from the generic scan above (see <see cref="ExcludedPaths"/>);
    /// covered directly here with a value that clears the field's own
    /// validation floor.
    /// </summary>
    [Fact]
    public void DefaultMaxOutputBytes_binds_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{AgentPrismOptions.SectionName}:Tools:DefaultMaxOutputBytes"] = "500",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentPrism(configuration.GetSection(AgentPrismOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var bound = provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value;

        bound.Tools.DefaultMaxOutputBytes.ShouldBe(500);
    }

    private static void Collect(
        object instance,
        string configPrefix,
        string[] propertyPath,
        Dictionary<string, string?> configValues,
        List<(string Path, PropertyInfo Property, object Expected)> expectations)
    {
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
            {
                continue;
            }

            var path = propertyPath.Length == 0 ? property.Name : string.Join('.', propertyPath) + "." + property.Name;

            if (ExcludedPaths.Contains(path))
            {
                continue;
            }

            var value = property.GetValue(instance);

            if (IsLeafType(property.PropertyType))
            {
                if (!property.CanWrite)
                {
                    continue;
                }

                var (configValue, expected) = GenerateSentinel(property.PropertyType, value);
                configValues[configPrefix + ":" + property.Name] = configValue;
                expectations.Add((path, property, expected));
                continue;
            }

            if (IsRecursableOptionType(property.PropertyType) && value is not null)
            {
                Collect(value, configPrefix + ":" + property.Name, [.. propertyPath, property.Name], configValues, expectations);
                continue;
            }

            throw new InvalidOperationException(
                $"'{path}' ({property.PropertyType}) is neither a scalar nor a known nested-options type. " +
                "It must be brought into Bind() scope (leaf) or added to ExcludedPaths with a reason.");
        }
    }

    private static object ResolveValue(object root, string path, PropertyInfo leafProperty)
    {
        var segments = path.Split('.');
        var current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current.GetType().GetProperty(segments[i])!.GetValue(current)!;
        }

        return leafProperty.GetValue(current)!;
    }

    private static bool IsLeafType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(bool)
            || underlying == typeof(int)
            || underlying == typeof(long)
            || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying == typeof(string)
            || underlying == typeof(TimeSpan)
            || underlying.IsEnum;
    }

    private static bool IsRecursableOptionType(Type type)
        => type.IsClass
            && string.Equals(type.Namespace, "AgentPrism", StringComparison.Ordinal)
            && type.Name.StartsWith("AgentPrism", StringComparison.Ordinal)
            && type.Name.EndsWith("Options", StringComparison.Ordinal)
            && type.GetConstructor(Type.EmptyTypes) is not null;

    private static (string ConfigValue, object Expected) GenerateSentinel(Type propertyType, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying == typeof(bool))
        {
            var next = current is not true;

            return (next.ToString(), next);
        }

        if (underlying == typeof(int))
        {
            var next = (current as int? ?? 0) + 7;

            return (next.ToString(CultureInfo.InvariantCulture), next);
        }

        if (underlying == typeof(long))
        {
            var next = (current as long? ?? 0L) + 7L;

            return (next.ToString(CultureInfo.InvariantCulture), next);
        }

        if (underlying == typeof(double))
        {
            var next = (current as double? ?? 0d) + 0.0123;

            return (next.ToString(CultureInfo.InvariantCulture), next);
        }

        if (underlying == typeof(decimal))
        {
            var next = (current as decimal? ?? 0m) + 0.5m;

            return (next.ToString(CultureInfo.InvariantCulture), next);
        }

        if (underlying == typeof(TimeSpan))
        {
            var next = (current as TimeSpan? ?? TimeSpan.Zero) + TimeSpan.FromMinutes(7) + TimeSpan.FromSeconds(3);

            return (next.ToString(), next);
        }

        if (underlying == typeof(string))
        {
            var next = "sentinel-" + Guid.NewGuid().ToString("N")[..8];

            return (next, next);
        }

        if (underlying.IsEnum)
        {
            var values = Enum.GetValues(underlying).Cast<object>().ToArray();
            var next = values.First(candidate => !candidate.Equals(current));

            return (next.ToString()!, next);
        }

        throw new NotSupportedException($"Sentinel type not supported: {underlying}");
    }
}
