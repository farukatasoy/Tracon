using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Configuration;

/// <summary>
/// <c>AgentPrismOptions</c> elle baglanir (K-021, AOT). Bu, "yeni alan eklendi
/// ama <c>Bind()</c>'a eklenmedi" kusurunu yapisal kilar — ayni kusur bagimsiz
/// uc kez bulundu: <c>RunRecording.RecordRunInput</c> (K-406),
/// <c>Observability.IncludeAgentVersionTag</c> (MT-OBS-036) ve
/// <c>Validation.McpTimeout</c> (K-253 — tanimlanip hic baglanmamis).
/// </summary>
/// <remarks>
/// Bu test <c>AgentPrismOptions</c> agacindaki HER skalar (bool/int/long/double/
/// decimal/string/TimeSpan/enum) alanı tek bir yapilandirma kumesiyle doldurup
/// geri okuyarak <c>Bind()</c>'in gercekten her alani islediginin kalici
/// kanitidir — tek tek regresyon testi eklemek yerine, gelecekte eklenen bir
/// alan Bind()'a eklenmeyi unutulursa bu test kirilir.
/// </remarks>
public sealed class AgentPrismOptionsBindingCoverageTests
{
    /// <summary>
    /// Sozluk/koleksiyon tipli alanlar (<c>Providers</c>, <c>Voice</c>,
    /// <c>AllowedMediaTypes</c>, <c>SkillRoots</c>, <c>Interpreters</c>,
    /// <c>EnvironmentAllowList</c>) ve ozel donusum yapan <c>Pricing</c>/
    /// <c>UtilityModel</c> kendi bagimsiz testleriyle (BindPricing/BindUtilityModel
    /// senaryolari, Aile O saglayici testleri) zaten kapsanir; bu genel tarayici
    /// yalniz "duz" (property adi = yapilandirma anahtari) baglamayi dogrular.
    /// </summary>
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.Ordinal)
    {
        "Pricing",
        "UtilityModel",
        "Skills.Scripts.SkillRoots",
        "Skills.Scripts.Interpreters",
        "Skills.Scripts.EnvironmentAllowList",
        "Attachments.AllowedMediaTypes",
    };

    [Fact]
    public void AgentPrismOptions_agacindaki_her_skalar_alan_yapilandirmadan_baglanir()
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
                failures.Add($"'{path}': beklenen '{expected}', gelen '{actual}' — Bind()'da unutulmus olabilir.");
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join('\n', failures));
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
                $"'{path}' ({property.PropertyType}) ne skalar ne bilinen bir ic-ayar tipi. " +
                "Bind() kapsamina alinmali (leaf) veya ExcludedPaths'e gerekceyle eklenmeli.");
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

        throw new NotSupportedException($"Sentinel turu desteklenmiyor: {underlying}");
    }
}
