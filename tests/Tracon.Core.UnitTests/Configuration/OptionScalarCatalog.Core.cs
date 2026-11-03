using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>The scope of this project: the sections of <c>Tracon.Core</c> and <c>Tracon.Abstractions</c>.</summary>
internal static partial class OptionScalarCatalog
{
    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeLiveVoice =
        static (builder, section) => builder.UseLiveVoice(section.GetSection("Voice:Live"));

    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeVoiceConversation =
        static (builder, section) => builder.UseVoiceConversation(section.GetSection("Voice:Conversation"));

    private static partial IEnumerable<Type> SectionTypes()
        => new[] { typeof(TraconOptions).Assembly, typeof(TraconSchedulingOptions).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsPublic: true, IsAbstract: false })
            .Where(static type => type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)
                is { IsLiteral: true } field
                && field.GetRawConstantValue() is string name
                && name.StartsWith("Tracon", StringComparison.Ordinal))
            .Where(static type => type.GetConstructor(Type.EmptyTypes) is not null)
            .Distinct();

    private static partial Action<ITraconBuilder, IConfigurationSection>? ComposeFor(Type type)
        => type == typeof(VoiceLiveOptions)
            ? ComposeLiveVoice
            : type == typeof(VoiceConversationOptions) ? ComposeVoiceConversation : null;

    private static partial IReadOnlyDictionary<string, string?> BaseKeysFor(Type type)
        => new Dictionary<string, string?>(StringComparer.Ordinal);
}

internal sealed partial record OptionScalar
{
    private static readonly Dictionary<string, string> CoreCompanions = new(StringComparer.Ordinal)
    {
        // Script execution refuses to start without this acknowledgement.
        ["Tracon:Skills:Scripts:PlatformIsolationAcknowledged"] = "true",

        // Image generation refuses to start without a provider and a model.
        ["Tracon:Images:Provider"] = "probe-provider",
        ["Tracon:Images:Model"] = "probe-model",

        // Content protection refuses to start without an active key.
        ["Tracon:ContentProtection:ActiveKeyId"] = "probe-key",
    };

    private static partial IReadOnlyDictionary<string, string> Companions() => CoreCompanions;
}
