using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every public settable scalar of every <c>Tracon:</c> section type of <c>Tracon.Core</c> and
/// <c>Tracon.Abstractions</c>, nested options objects included, found by reflection.
/// </summary>
/// <remarks>
/// The binders are written by hand, so the list of keys cannot be read from them. Reading it from the
/// options types is what lets a probe cover a scalar added tomorrow without anyone editing the probe.
/// </remarks>
internal static class OptionScalarCatalog
{
    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeLiveVoice =
        static (builder, section) => builder.UseLiveVoice(section.GetSection("Voice:Live"));

    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeVoiceConversation =
        static (builder, section) => builder.UseVoiceConversation(section.GetSection("Voice:Conversation"));

    /// <summary>Lists the scalars of every section type.</summary>
    /// <returns>The scalars, a number-like or text one each.</returns>
    public static IReadOnlyList<OptionScalar> All()
    {
        var scalars = new List<OptionScalar>();

        foreach (var type in SectionTypes())
        {
            var sectionName = (string)type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

            Collect(type, Activator.CreateInstance(type)!, sectionName, type.Name, [], ComposeFor(type), scalars);
        }

        return scalars;
    }

    /// <summary>Whether the type is one the binders read from text as a value.</summary>
    /// <param name="type">The property type.</param>
    /// <returns><see langword="true"/> for a boolean, a number, a time span or an enum.</returns>
    public static bool IsReadScalar(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

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
        Type rootType,
        object instance,
        string path,
        string display,
        PropertyInfo[] chain,
        Action<ITraconBuilder, IConfigurationSection>? compose,
        List<OptionScalar> scalars)
    {
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true })
            {
                continue;
            }

            var key = $"{path}:{property.Name}";
            var member = $"{display}.{property.Name}";
            var propertyChain = chain.Append(property).ToArray();

            if (IsReadScalar(property.PropertyType) || property.PropertyType == typeof(string))
            {
                if (property.SetMethod is { IsPublic: true })
                {
                    scalars.Add(new OptionScalar(rootType, key, member, property.PropertyType, propertyChain, property.GetValue(instance), compose));
                }

                continue;
            }

            if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) || !IsNestedOptions(property.PropertyType))
            {
                continue;
            }

            if (property.GetValue(instance) is { } nested)
            {
                Collect(rootType, nested, key, member, propertyChain, compose, scalars);
            }
        }
    }

    private static bool IsNestedOptions(Type type)
        => type.IsClass
            && string.Equals(type.Namespace, "Tracon", StringComparison.Ordinal)
            && type.Name.EndsWith("Options", StringComparison.Ordinal);
}

/// <summary>One scalar a configuration key can set.</summary>
/// <param name="RootType">The options type the container registers (the section type).</param>
/// <param name="Key">The configuration key written in full, such as <c>Tracon:Tools:DefaultTimeout</c>.</param>
/// <param name="Display">The type and member name, for failure messages.</param>
/// <param name="PropertyType">The declared type of the member.</param>
/// <param name="Chain">The members from the root options object down to the scalar.</param>
/// <param name="Default">The value a new instance holds.</param>
/// <param name="Compose">The rest of the Tracon chain, for a section another call binds.</param>
internal sealed record OptionScalar(
    Type RootType,
    string Key,
    string Display,
    Type PropertyType,
    PropertyInfo[] Chain,
    object? Default,
    Action<ITraconBuilder, IConfigurationSection>? Compose)
{
    private static readonly Dictionary<string, string> FeatureCompanions = new(StringComparer.Ordinal)
    {
        // Script execution refuses to start without this acknowledgement.
        ["Tracon:Skills:Scripts:PlatformIsolationAcknowledged"] = "true",

        // Image generation refuses to start without a provider and a model.
        ["Tracon:Images:Provider"] = "probe-provider",
        ["Tracon:Images:Model"] = "probe-model",

        // Content protection refuses to start without an active key.
        ["Tracon:ContentProtection:ActiveKeyId"] = "probe-key",
    };

    /// <summary>Gets the type the binder reads: the declared type without <see cref="Nullable{T}"/>.</summary>
    public Type ScalarType => Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;

    /// <summary>Gets a value indicating whether the binder reads the member as a value (not as text).</summary>
    public bool IsReadScalar => OptionScalarCatalog.IsReadScalar(PropertyType);

    /// <summary>
    /// Gets the keys that switch the feature of this scalar on: the boolean switches next to it in the same
    /// options object, and the companions a feature needs before it starts. Many validators check a value
    /// only while its feature is on, so a probe with the feature off proves nothing about them.
    /// </summary>
    /// <returns>The keys with the values that switch the feature on; empty when the scalar has no switch.</returns>
    public IReadOnlyDictionary<string, string?> EnablingKeys()
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var parent = Key[..Key.LastIndexOf(':')];
        var owner = Chain.Length > 1 ? Chain[^2].PropertyType : RootType;

        foreach (var property in owner.GetProperties())
        {
            if (property.PropertyType == typeof(bool)
                && !string.Equals(property.Name, Chain[^1].Name, StringComparison.Ordinal)
                && (property.Name.StartsWith("Enable", StringComparison.Ordinal)
                    || property.Name.EndsWith("Enabled", StringComparison.Ordinal)))
            {
                result[$"{parent}:{property.Name}"] = "true";
            }
        }

        if (result.Count == 0)
        {
            return result;
        }

        foreach (var (key, value) in FeatureCompanions)
        {
            if (key.StartsWith(parent + ":", StringComparison.Ordinal)
                && !string.Equals(key, Key, StringComparison.Ordinal)
                && key.AsSpan(parent.Length + 1).IndexOf(':') < 0)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Reads the member from the options object the container built.</summary>
    /// <param name="root">The registered options object.</param>
    /// <returns>The member's value.</returns>
    public object? ReadFrom(object root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var current = root;

        foreach (var property in Chain)
        {
            current = property.GetValue(current);
        }

        return current;
    }
}
