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
internal static partial class OptionScalarCatalog
{
    /// <summary>Lists the scalars of every section type.</summary>
    /// <returns>The scalars, a number-like or text one each.</returns>
    public static IReadOnlyList<OptionScalar> All()
    {
        var scalars = new List<OptionScalar>();

        foreach (var type in SectionTypes())
        {
            var sectionName = (string)type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

            Collect(type, Activator.CreateInstance(type)!, sectionName, type.Name, [], ComposeFor(type), BaseKeysFor(type), scalars);
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

    /// <summary>The section types this project walks: the scope of the project says which assemblies they live in.</summary>
    /// <returns>The public options classes that carry a <c>SectionName</c> constant.</returns>
    private static partial IEnumerable<Type> SectionTypes();

    /// <summary>The rest of the Tracon chain a section needs, for a section another call binds (voice, a provider, a store).</summary>
    /// <param name="type">The section type.</param>
    /// <returns>The call that binds the section, or <see langword="null"/> when <c>AddTracon</c> binds it.</returns>
    private static partial Action<ITraconBuilder, IConfigurationSection>? ComposeFor(Type type);

    /// <summary>The keys a section needs in order to start at all (a connection string, an API key), written whatever the probe does.</summary>
    /// <param name="type">The section type.</param>
    /// <returns>The keys written in full, with their values.</returns>
    private static partial IReadOnlyDictionary<string, string?> BaseKeysFor(Type type);

    private static void Collect(
        Type rootType,
        object instance,
        string path,
        string display,
        PropertyInfo[] chain,
        Action<ITraconBuilder, IConfigurationSection>? compose,
        IReadOnlyDictionary<string, string?> baseKeys,
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
                    scalars.Add(new OptionScalar(rootType, key, member, property.PropertyType, propertyChain, property.GetValue(instance), compose, baseKeys));
                }

                continue;
            }

            if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) || !IsNestedOptions(property.PropertyType))
            {
                continue;
            }

            if (property.GetValue(instance) is { } nested)
            {
                Collect(rootType, nested, key, member, propertyChain, compose, baseKeys, scalars);
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
/// <param name="BaseKeys">The keys the section needs in order to start at all, written with every probe.</param>
internal sealed partial record OptionScalar(
    Type RootType,
    string Key,
    string Display,
    Type PropertyType,
    PropertyInfo[] Chain,
    object? Default,
    Action<ITraconBuilder, IConfigurationSection>? Compose,
    IReadOnlyDictionary<string, string?> BaseKeys)
{
    /// <summary>
    /// The keys a feature needs before it starts, written when the probe switches the feature on. The scope of the
    /// project lists them.
    /// </summary>
    private static partial IReadOnlyDictionary<string, string> Companions();

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

        foreach (var (key, value) in Companions())
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
