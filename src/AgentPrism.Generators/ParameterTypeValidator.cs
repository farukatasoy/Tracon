using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators;

/// <summary>
/// Converts a method parameter to <see cref="ParameterModel"/>. Returns
/// <see langword="null"/> for a type outside the allow list (APG0003).
/// </summary>
internal static class ParameterTypeValidator
{
    private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";
    private const string GuidMetadataName = "System.Guid";
    private const string DateTimeMetadataName = "System.DateTime";
    private const string DateTimeOffsetMetadataName = "System.DateTimeOffset";
    private const string DescriptionAttributeMetadataName = "System.ComponentModel.DescriptionAttribute";

    /// <summary>
    /// Classifies a parameter. When <paramref name="parameter"/> is
    /// <c>System.Threading.CancellationToken</c>, returns
    /// <see cref="ParameterShape.CancellationToken"/> and excludes it from the JSON schema.
    /// </summary>
    public static ParameterModel? TryCreate(IParameterSymbol parameter)
    {
        var type = parameter.Type;

        if (string.Equals(GetMetadataName(type), CancellationTokenMetadataName, StringComparison.Ordinal))
        {
            return new ParameterModel(parameter.Name, ParameterShape.CancellationToken, Leaf: null, IsRequired: true, DefaultValueLiteral: null);
        }

        var isRequired = !parameter.HasExplicitDefaultValue;
        var defaultLiteral = parameter.HasExplicitDefaultValue
            ? RenderDefaultValueLiteral(parameter.ExplicitDefaultValue, type)
            : null;
        var description = ReadDescription(parameter);

        if (TryGetArrayElementType(type, out var elementType, out var isConcreteArray))
        {
            var elementLeaf = TryCreateLeaf(elementType);
            return elementLeaf is null
                ? null
                : new ParameterModel(parameter.Name, ParameterShape.Array, elementLeaf, isRequired, defaultLiteral, isConcreteArray, description);
        }

        var leaf = TryCreateLeaf(type);
        return leaf is null ? null : new ParameterModel(parameter.Name, ParameterShape.Scalar, leaf, isRequired, defaultLiteral, IsConcreteArray: false, Description: description);
    }

    /// <summary>
    /// Reads <see cref="System.ComponentModel.DescriptionAttribute"/> from the parameter.
    /// AgentPrism ships no attribute of its own for this (125.1): the BCL attribute is
    /// also read by <c>Microsoft.Extensions.AI.AIFunctionFactory.Create</c>, so both
    /// tool-writing paths teach a consumer the same rule.
    /// </summary>
    private static string? ReadDescription(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            var metadataName = $"{attributeClass.ContainingNamespace}.{attributeClass.Name}";

            if (!string.Equals(metadataName, DescriptionAttributeMetadataName, StringComparison.Ordinal))
            {
                continue;
            }

            return attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string description
                ? (string.IsNullOrWhiteSpace(description) ? null : description)
                : null;
        }

        return null;
    }

    private static LeafType? TryCreateLeaf(ITypeSymbol type)
    {
        var isNullable = false;
        var effectiveType = type;

        if (effectiveType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            isNullable = true;
            effectiveType = nullable.TypeArguments[0];
        }

        if (effectiveType.TypeKind == TypeKind.Enum)
        {
            var memberNames = effectiveType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.ConstantValue is not null)
                .Select(f => f.Name)
                .ToArray();

            return LeafType.Enum(GetFullyQualifiedName(effectiveType), isNullable, System.Collections.Immutable.ImmutableArray.Create(memberNames));
        }

        var kind = effectiveType.SpecialType switch
        {
            SpecialType.System_Boolean => LeafTypeKind.Boolean,
            SpecialType.System_SByte or SpecialType.System_Byte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64 => LeafTypeKind.Integer,
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal => LeafTypeKind.Number,
            SpecialType.System_String => LeafTypeKind.String,
            _ => (LeafTypeKind?)null,
        } ?? GetMetadataName(effectiveType) switch
        {
            GuidMetadataName => LeafTypeKind.Guid,
            DateTimeMetadataName => LeafTypeKind.DateTime,
            DateTimeOffsetMetadataName => LeafTypeKind.DateTimeOffset,
            _ => (LeafTypeKind?)null,
        };

        return kind is null ? null : LeafType.Scalar(kind.Value, GetFullyQualifiedName(effectiveType), isNullable);
    }

    private static bool TryGetArrayElementType(ITypeSymbol type, out ITypeSymbol elementType, out bool isConcreteArray)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            elementType = array.ElementType;
            isConcreteArray = true;
            return true;
        }

        isConcreteArray = false;

        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named)
        {
            var definitionName = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None));

            if (definitionName is "global::System.Collections.Generic.IReadOnlyList" or
                "global::System.Collections.Generic.IReadOnlyCollection" or
                "global::System.Collections.Generic.IList" or
                "global::System.Collections.Generic.IEnumerable" or
                "global::System.Collections.Generic.List")
            {
                elementType = named.TypeArguments[0];
                return true;
            }
        }

        elementType = type;
        return false;
    }

    private static string? GetMetadataName(ITypeSymbol type)
        => type is INamedTypeSymbol named ? $"{named.ContainingNamespace}.{named.Name}" : null;

    /// <summary>
    /// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> writes primitive types
    /// as C# keywords such as "int" because of <c>UseSpecialTypes</c>, not as
    /// "global::System.Int32". Type-name switching in <see cref="SourceWriter"/>,
    /// which selects the numeric converter, depends on consistent CLR names. This
    /// format suppresses keywords.
    /// </summary>
    private static readonly SymbolDisplayFormat FullyQualifiedClrFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static string GetFullyQualifiedName(ITypeSymbol type)
        => type.ToDisplayString(FullyQualifiedClrFormat);

    /// <summary>
    /// Converts a C# parameter default value to a source-text literal that generated code can use.
    /// </summary>
    private static string RenderDefaultValueLiteral(object? value, ITypeSymbol type)
    {
        if (value is null)
        {
            return "null";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => ToStringLiteral(s),
            char c => $"'{c}'",
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
            _ when type.TypeKind == TypeKind.Enum => $"({GetFullyQualifiedName(type)})({Convert.ToInt64(value, CultureInfo.InvariantCulture)})",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
    }

    private static string ToStringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
