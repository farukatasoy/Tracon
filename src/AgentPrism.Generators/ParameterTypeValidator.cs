using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators;

/// <summary>
/// Bir metot parametresini <see cref="ParameterModel"/>'e cevirir. Beyaz liste
/// disindaki bir tip icin <see langword="null"/> doner (APG0003).
/// </summary>
internal static class ParameterTypeValidator
{
    private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";
    private const string GuidMetadataName = "System.Guid";
    private const string DateTimeMetadataName = "System.DateTime";
    private const string DateTimeOffsetMetadataName = "System.DateTimeOffset";

    /// <summary>
    /// Parametreyi siniflandirir. <paramref name="parameter"/>
    /// <c>System.Threading.CancellationToken</c> ise <see cref="ParameterShape.CancellationToken"/>
    /// doner ve JSON semasina girmez.
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

        if (TryGetArrayElementType(type, out var elementType))
        {
            var elementLeaf = TryCreateLeaf(elementType);
            return elementLeaf is null
                ? null
                : new ParameterModel(parameter.Name, ParameterShape.Array, elementLeaf, isRequired, defaultLiteral);
        }

        var leaf = TryCreateLeaf(type);
        return leaf is null ? null : new ParameterModel(parameter.Name, ParameterShape.Scalar, leaf, isRequired, defaultLiteral);
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

    private static bool TryGetArrayElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            elementType = array.ElementType;
            return true;
        }

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
    /// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>, <c>UseSpecialTypes</c>
    /// yuzunden ilkel tipleri C# anahtar kelimesiyle ("int") yazar, "global::System.Int32"
    /// ile DEGIL. <see cref="SourceWriter"/>'daki tip-adi anahtarlamasi (numerik
    /// donusturucu secimi) tutarli CLR adlarina bagimlidir; bu format anahtar
    /// kelimeleri BASTIRIR.
    /// </summary>
    private static readonly SymbolDisplayFormat FullyQualifiedClrFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static string GetFullyQualifiedName(ITypeSymbol type)
        => type.ToDisplayString(FullyQualifiedClrFormat);

    /// <summary>
    /// C# parametre varsayilan degerini uretilen kodda kullanilabilecek bir
    /// kaynak metin literaline cevirir.
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
