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
    private const string RangeAttributeMetadataName = "System.ComponentModel.DataAnnotations.RangeAttribute";
    private const string MinLengthAttributeMetadataName = "System.ComponentModel.DataAnnotations.MinLengthAttribute";
    private const string MaxLengthAttributeMetadataName = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
    private const string StringLengthAttributeMetadataName = "System.ComponentModel.DataAnnotations.StringLengthAttribute";
    private const string RegularExpressionAttributeMetadataName = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute";

    /// <summary>
    /// Classifies a parameter. When <paramref name="parameter"/> is
    /// <c>System.Threading.CancellationToken</c>, returns
    /// <see cref="ParameterShape.CancellationToken"/> and excludes it from the JSON schema.
    /// </summary>
    /// <param name="unsupportedConstraintAttributes">
    /// Constraint attributes (130.2) present on <paramref name="parameter"/> that do not
    /// apply to its resolved type or shape - reported as APG0010 by the caller. A
    /// constraint attribute never blocks generation, so this is populated independently of
    /// the return value, including when the return value is <see langword="null"/>.
    /// </param>
    public static ParameterModel? TryCreate(IParameterSymbol parameter, out EquatableArray<string> unsupportedConstraintAttributes)
    {
        var type = parameter.Type;

        if (string.Equals(GetMetadataName(type), CancellationTokenMetadataName, StringComparison.Ordinal))
        {
            unsupportedConstraintAttributes = EquatableArray<string>.Empty;
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

            if (elementLeaf is null)
            {
                unsupportedConstraintAttributes = EquatableArray<string>.Empty;
                return null;
            }

            var arrayConstraints = ReadConstraints(parameter, ParameterShape.Array, elementLeaf.Kind, out unsupportedConstraintAttributes);
            return new ParameterModel(parameter.Name, ParameterShape.Array, elementLeaf, isRequired, defaultLiteral, isConcreteArray, description, arrayConstraints);
        }

        var leaf = TryCreateLeaf(type);

        if (leaf is null)
        {
            unsupportedConstraintAttributes = EquatableArray<string>.Empty;
            return null;
        }

        var constraints = ReadConstraints(parameter, ParameterShape.Scalar, leaf.Kind, out unsupportedConstraintAttributes);
        return new ParameterModel(parameter.Name, ParameterShape.Scalar, leaf, isRequired, defaultLiteral, IsConcreteArray: false, Description: description, Constraints: constraints);
    }

    /// <summary>
    /// Reads JSON Schema constraints (130.1) from standard
    /// <c>System.ComponentModel.DataAnnotations</c> attributes. An attribute that does not
    /// apply to <paramref name="shape"/>/<paramref name="leafKind"/> contributes nothing to
    /// the returned <see cref="ParameterConstraints"/> and is instead named in
    /// <paramref name="unsupportedAttributes"/> for the caller to report as APG0010 -
    /// this never blocks generation.
    /// </summary>
    /// <remarks>
    /// A length constraint on an array parameter (<see cref="MinLengthAttribute"/>/
    /// <see cref="MaxLengthAttribute"/>) targets the array itself
    /// (<c>minItems</c>/<c>maxItems</c>), never its element - "at least two tags", not
    /// "each tag at least two characters" (130.3). A range constraint targets the leaf
    /// value regardless of shape, because its meaning does not change between a scalar and
    /// an array element the way a length constraint's does.
    /// </remarks>
    private static ParameterConstraints ReadConstraints(
        IParameterSymbol parameter,
        ParameterShape shape,
        LeafTypeKind leafKind,
        out EquatableArray<string> unsupportedAttributes)
    {
        string? minimum = null;
        string? maximum = null;
        int? minLength = null;
        int? maxLength = null;
        int? minItems = null;
        int? maxItems = null;
        string? pattern = null;
        var unsupported = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            var metadataName = $"{attributeClass.ContainingNamespace}.{attributeClass.Name}";

            switch (metadataName)
            {
                case RangeAttributeMetadataName:
                    if (leafKind is LeafTypeKind.Integer or LeafTypeKind.Number && TryReadRange(attribute, out var rangeMinimum, out var rangeMaximum))
                    {
                        minimum = rangeMinimum;
                        maximum = rangeMaximum;
                    }
                    else
                    {
                        unsupported.Add("[Range]");
                    }

                    break;

                case MinLengthAttributeMetadataName:
                    // JSON Schema requires minLength/minItems to be a non-negative integer
                    // (draft 2020-12, "nonNegativeInteger"); a negative constant is not a
                    // missing-constructor-argument case like MaxLengthAttribute() below, but
                    // it is equally unrenderable, so it is reported the same way (APG0010)
                    // instead of being written into an otherwise-invalid schema.
                    if (!TryReadSingleIntArgument(attribute, out var minLengthValue) || minLengthValue < 0)
                    {
                        unsupported.Add("[MinLength]");
                    }
                    else if (shape == ParameterShape.Array)
                    {
                        minItems = MergeNarrowerMinimum(minItems, minLengthValue);
                    }
                    else if (leafKind == LeafTypeKind.String)
                    {
                        minLength = MergeNarrowerMinimum(minLength, minLengthValue);
                    }
                    else
                    {
                        unsupported.Add("[MinLength]");
                    }

                    break;

                case MaxLengthAttributeMetadataName:
                    // TryReadSingleIntArgument also fails for MaxLengthAttribute's
                    // parameterless constructor (MaxLengthAttribute(), a valid C# usage
                    // meaning "use the store's own maximum") - it carries no length value
                    // for the generator to render, so it is reported rather than silently
                    // producing no schema effect.
                    if (!TryReadSingleIntArgument(attribute, out var maxLengthValue) || maxLengthValue < 0)
                    {
                        unsupported.Add("[MaxLength]");
                    }
                    else if (shape == ParameterShape.Array)
                    {
                        maxItems = MergeNarrowerMaximum(maxItems, maxLengthValue);
                    }
                    else if (leafKind == LeafTypeKind.String)
                    {
                        maxLength = MergeNarrowerMaximum(maxLength, maxLengthValue);
                    }
                    else
                    {
                        unsupported.Add("[MaxLength]");
                    }

                    break;

                case StringLengthAttributeMetadataName:
                    {
                        var hasMaxLength = attribute.ConstructorArguments.Length > 0 &&
                            attribute.ConstructorArguments[0].Value is int rawMaxLength &&
                            rawMaxLength >= 0;
                        var stringMaxLength = hasMaxLength ? (int)attribute.ConstructorArguments[0].Value! : 0;

                        int? stringMinLength = null;
                        var hasInvalidMinimumLength = false;

                        foreach (var named in attribute.NamedArguments)
                        {
                            if (!string.Equals(named.Key, "MinimumLength", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (named.Value.Value is int namedMinLength && namedMinLength >= 0)
                            {
                                stringMinLength = namedMinLength;
                            }
                            else
                            {
                                hasInvalidMinimumLength = true;
                            }
                        }

                        if (shape == ParameterShape.Scalar && leafKind == LeafTypeKind.String && hasMaxLength && !hasInvalidMinimumLength)
                        {
                            maxLength = MergeNarrowerMaximum(maxLength, stringMaxLength);

                            if (stringMinLength is { } validMinLength)
                            {
                                minLength = MergeNarrowerMinimum(minLength, validMinLength);
                            }
                        }
                        else
                        {
                            unsupported.Add("[StringLength]");
                        }

                        break;
                    }

                case RegularExpressionAttributeMetadataName:
                    if (shape == ParameterShape.Scalar && leafKind == LeafTypeKind.String &&
                        attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string patternValue)
                    {
                        pattern = patternValue;
                    }
                    else
                    {
                        unsupported.Add("[RegularExpression]");
                    }

                    break;
            }
        }

        unsupportedAttributes = unsupported.ToImmutable();

        return new ParameterConstraints(minimum, maximum, minLength, maxLength, minItems, maxItems, pattern);
    }

    /// <summary>
    /// Reads <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/>'s numeric
    /// bounds. Returns <see langword="false"/> for its <c>Range(Type, string, string)</c>
    /// overload: that form's bounds are not a compile-time numeric constant, so the
    /// generator cannot render them (130.2).
    /// </summary>
    private static bool TryReadRange(AttributeData attribute, out string? minimum, out string? maximum)
    {
        minimum = null;
        maximum = null;

        var ctorArgs = attribute.ConstructorArguments;

        if (ctorArgs.Length != 2)
        {
            return false;
        }

        if (ctorArgs[0].Value is int minInt && ctorArgs[1].Value is int maxInt)
        {
            minimum = minInt.ToString(CultureInfo.InvariantCulture);
            maximum = maxInt.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (ctorArgs[0].Value is double minDouble && ctorArgs[1].Value is double maxDouble)
        {
            minimum = minDouble.ToString("R", CultureInfo.InvariantCulture);
            maximum = maxDouble.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static bool TryReadSingleIntArgument(AttributeData attribute, out int value)
    {
        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int argument)
        {
            value = argument;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Resolves two candidate values for the same "minimum" key (example: <c>[MinLength(2)]</c>
    /// and <c>[StringLength(10, MinimumLength = 3)]</c> on the same parameter) - the NARROWER
    /// bound wins, which for a minimum is the larger value (130.1). This is the single place
    /// that rule is applied.
    /// </summary>
    private static int MergeNarrowerMinimum(int? existing, int candidate) => existing is null ? candidate : Math.Max(existing.Value, candidate);

    /// <summary>The maximum counterpart of <see cref="MergeNarrowerMinimum"/> - the smaller value wins.</summary>
    private static int MergeNarrowerMaximum(int? existing, int candidate) => existing is null ? candidate : Math.Min(existing.Value, candidate);

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
