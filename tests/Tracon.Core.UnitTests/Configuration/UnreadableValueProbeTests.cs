using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every scalar a configuration section can set stops the host at startup when its text
/// cannot be read, and the message names the key and the text (F-572).
/// </summary>
/// <remarks>
/// <para>
/// The binders are written by hand. A scalar added to an options type and read with the
/// old skip-and-keep-the-default pattern, or not read at all, would compile and pass the
/// binding test that only writes valid text. This probe walks every public settable
/// scalar of every <c>Tracon:</c> section type in <c>Tracon.Core</c> and
/// <c>Tracon.Abstractions</c> (nested options objects too), writes text no type can
/// read into that one key, and expects the host to refuse to start with the key in the
/// message.
/// </para>
/// <para>
/// The package option types (providers, MCP, voice, the SQL stores) are probed by the
/// binding tests of their own packages; the source scan
/// (<c>UnreadableConfigurationSiteTests</c>) keeps their binders on the same reader.
/// </para>
/// </remarks>
public sealed class UnreadableValueProbeTests
{
    private const string Unreadable = "not-readable";

    /// <summary>Scalars that are NOT read from configuration on purpose, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotBoundOnPurpose = new(StringComparer.Ordinal)
    {
        // Widens what may be executed on the server; script execution is turned on in code only.
        ["Tracon:Skills:Scripts:AllowStoredScripts"] = "a security boundary: configuration must not widen the executable surface",
    };

    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeLiveVoice =
        static (builder, section) => builder.UseLiveVoice(section.GetSection("Voice:Live"));

    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeVoiceConversation =
        static (builder, section) => builder.UseVoiceConversation(section.GetSection("Voice:Conversation"));

    [Fact]
    public void Every_scalar_of_every_section_stops_the_host_when_its_text_cannot_be_read()
    {
        var probes = new List<Probe>();

        foreach (var type in SectionTypes())
        {
            var sectionName = (string)type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

            Collect(Activator.CreateInstance(type)!, sectionName, type.Name, ComposeFor(type), probes);
        }

        // A scan that found little would pass for the wrong reason.
        probes.Count.ShouldBeGreaterThan(150);

        var failures = new List<string>();

        foreach (var probe in probes)
        {
            if (NotBoundOnPurpose.ContainsKey(probe.Key))
            {
                continue;
            }

            try
            {
                var failure = SectionBinding.FailsAtStartup(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { [probe.Key] = Unreadable },
                    probe.Compose);

                if (!failure.Message.Contains($"{probe.Key} is '{Unreadable}'", StringComparison.Ordinal))
                {
                    failures.Add($"{probe.Display}: the host stopped, but the message does not name '{probe.Key}': {failure.Message}");
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failures.Add($"{probe.Display} ({probe.Key}): the host did not stop for text it cannot read. {exception.GetType().Name}");
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A scalar is read with the skip-and-keep-the-default pattern, or not read at all:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_exemption_names_a_real_scalar()
    {
        var keys = new List<Probe>();

        foreach (var type in SectionTypes())
        {
            var sectionName = (string)type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

            Collect(Activator.CreateInstance(type)!, sectionName, type.Name, ComposeFor(type), keys);
        }

        foreach (var exempt in NotBoundOnPurpose.Keys)
        {
            keys.ShouldContain(probe => string.Equals(probe.Key, exempt, StringComparison.Ordinal), $"'{exempt}' is exempt, but no scalar has that key.");
        }
    }

    private static Action<ITraconBuilder, IConfigurationSection>? ComposeFor(Type type)
        => type == typeof(VoiceLiveOptions)
            ? ComposeLiveVoice
            : type == typeof(VoiceConversationOptions) ? ComposeVoiceConversation : null;

    private static IEnumerable<Type> SectionTypes()
        => new[] { typeof(TraconOptions).Assembly, typeof(TraconSchedulingOptions).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsPublic: true, IsAbstract: false })
            .Where(static type => type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)
                is { IsLiteral: true } field
                && field.GetRawConstantValue() is string name
                && name.StartsWith("Tracon", StringComparison.Ordinal))
            .Where(static type => type.GetConstructor(Type.EmptyTypes) is not null)
            .Distinct();

    private static void Collect(
        object instance,
        string path,
        string display,
        Action<ITraconBuilder, IConfigurationSection>? compose,
        List<Probe> probes)
    {
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true })
            {
                continue;
            }

            var key = $"{path}:{property.Name}";
            var member = $"{display}.{property.Name}";

            if (IsReadScalar(property.PropertyType))
            {
                if (property.SetMethod is { IsPublic: true })
                {
                    probes.Add(new Probe(key, member, compose));
                }

                continue;
            }

            if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) || !IsNestedOptions(property.PropertyType))
            {
                continue;
            }

            if (property.GetValue(instance) is { } nested)
            {
                Collect(nested, key, member, compose, probes);
            }
        }
    }

    private static bool IsReadScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(bool)
            || underlying == typeof(int)
            || underlying == typeof(long)
            || underlying == typeof(float)
            || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying == typeof(TimeSpan)
            || underlying.IsEnum;
    }

    private static bool IsNestedOptions(Type type)
        => type.IsClass
            && string.Equals(type.Namespace, "Tracon", StringComparison.Ordinal)
            && type.Name.EndsWith("Options", StringComparison.Ordinal);

    private sealed record Probe(string Key, string Display, Action<ITraconBuilder, IConfigurationSection>? Compose);
}
