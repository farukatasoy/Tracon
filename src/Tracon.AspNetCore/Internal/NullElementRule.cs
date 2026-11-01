using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Tracon;

/// <summary>
/// Makes a null inside a list or a map of a request body what a null property already is: a refused body.
/// </summary>
/// <remarks>
/// <para>
/// <c>RespectNullableAnnotations</c> refuses <c>{"items": null}</c> for a non-nullable property, but it never looks
/// inside a collection: <c>{"items": [null]}</c> reaches the handler as a list that holds a null, and the first line
/// that reads an element throws <see cref="NullReferenceException"/>, which the caller sees as a 500. Measured in
/// <c>System.Text.Json</c> on .NET 10 for a list, an array, a dictionary value and a list of strings alike.
/// </para>
/// <para>
/// The contracts hold more than a hundred collections and the handlers cannot be trusted to check each one, so the rule
/// is written once, from the property's own nullable annotation: a collection whose element (or, for a map, whose value)
/// is a non-nullable reference type may not hold a null. <c>string?</c>, <c>int?</c> and every value type keep what they
/// accept. A body that is itself a list is checked by <see cref="FindInRoot"/>, which has no annotation to read.
/// </para>
/// </remarks>
internal static class NullElementRule
{
    /// <summary>The contract customization to add to the request-body options.</summary>
    /// <param name="typeInfo">The contract being built.</param>
    internal static void Guard(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var context = new NullabilityInfoContext();
        List<JsonPropertyInfo>? guarded = null;

        foreach (var property in typeInfo.Properties)
        {
            if (property.Get is null
                || property.AttributeProvider is not PropertyInfo member
                || !HoldsOnlyNonNullableReferences(context.Create(member)))
            {
                continue;
            }

            (guarded ??= []).Add(property);
        }

        if (guarded is null)
        {
            return;
        }

        var previous = typeInfo.OnDeserialized;

        typeInfo.OnDeserialized = instance =>
        {
            previous?.Invoke(instance);

            foreach (var property in guarded)
            {
                if (property.Get!(instance) is { } value && HasNull(value))
                {
                    throw new JsonException($"The property '{property.Name}' cannot contain a null element.");
                }
            }
        };
    }

    /// <summary>Finds a null element in a body that is itself a list or a map.</summary>
    /// <param name="value">The deserialized body.</param>
    /// <returns>The sentence to put in the problem, or <see langword="null"/> when the body has no null element.</returns>
    /// <remarks>
    /// A root has no annotation, so the element type decides: a reference type other than <see cref="object"/> and the
    /// JSON node types, which are meant to hold nulls.
    /// </remarks>
    internal static string? FindInRoot(object? value)
    {
        if (value is null or string)
        {
            return null;
        }

        var type = value.GetType();
        var element = type.IsArray
            ? type.GetElementType()
            : (ElementOf(type, typeof(IDictionary<,>), 1) ?? ElementOf(type, typeof(IEnumerable<>), 0));

        return element is { IsValueType: false }
            && element != typeof(object)
            && !typeof(JsonNode).IsAssignableFrom(element)
            && HasNull(value)
                ? "The body cannot contain a null element."
                : null;
    }

    private static Type? ElementOf(Type type, Type definition, int argument)
        => type.GetInterfaces()
            .Concat([type])
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition)
            ?.GetGenericArguments()[argument];

    private static bool HoldsOnlyNonNullableReferences(NullabilityInfo info)
    {
        if (info.Type == typeof(string))
        {
            return false;
        }

        var element = info.ElementType
            ?? (info.GenericTypeArguments.Length > 0 && typeof(IEnumerable).IsAssignableFrom(info.Type)
                ? info.GenericTypeArguments[^1]
                : null);

        return element is { Type.IsValueType: false, ReadState: NullabilityState.NotNull }
            && element.Type != typeof(object)
            && !typeof(JsonNode).IsAssignableFrom(element.Type);
    }

    private static bool HasNull(object collection)
    {
        if (collection is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value is null)
                {
                    return true;
                }
            }

            return false;
        }

        if (collection is IEnumerable items and not string)
        {
            foreach (var item in items)
            {
                if (item is null)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
