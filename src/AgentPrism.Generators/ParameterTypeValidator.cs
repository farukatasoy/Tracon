using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators;

/// <summary>The kind of failure found while walking a parameter's object graph (135.1).</summary>
internal enum ObjectGraphErrorKind
{
    /// <summary>The graph is more than 3 nested object levels deep.</summary>
    DepthExceeded,

    /// <summary>A type appears again among its own ancestors.</summary>
    Cycle,
}

/// <summary>
/// A depth or cycle failure found while walking a parameter's object graph - reported by
/// the caller as APG0012. Never stored on a cached model; it only lives for the duration
/// of one <see cref="ParameterTypeValidator.TryCreate"/> call.
/// </summary>
internal sealed record ObjectGraphError(ObjectGraphErrorKind Kind, string PathText);

/// <summary>
/// Converts a method parameter to <see cref="ParameterModel"/>. Returns
/// <see langword="null"/> for a type outside the allow list (APG0003).
/// </summary>
internal static class ParameterTypeValidator
{
    /// <summary>
    /// 135.1: the root parameter is depth 0; its own object members are depth 1. A graph
    /// deeper than this is rejected (APG0012) instead of silently generated - the model's
    /// error rate rises with schema depth, and a graph this deep is usually a sign the tool
    /// is doing too much.
    /// </summary>
    private const int MaxObjectDepth = 3;

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
    /// Constraint attributes (130.2) present on <paramref name="parameter"/>, or anywhere in
    /// its object graph (135.2), that do not apply to their resolved type or shape - reported
    /// as APG0010 by the caller. A constraint attribute never blocks generation, so this is
    /// populated independently of the return value, including when the return value is
    /// <see langword="null"/>.
    /// </param>
    /// <param name="referencedObjectTypes">
    /// Every distinct object type (135.1) found in this parameter's graph, including the
    /// parameter's own type when it is itself an object. The caller cross-checks each one
    /// against the tool's <c>JsonSerializerContext</c> and reports APG0011 for any that is
    /// not declared with <c>[JsonSerializable]</c> - empty for a parameter with no object
    /// anywhere in its shape.
    /// </param>
    /// <param name="graphError">
    /// Set when the object graph is deeper than <see cref="MaxObjectDepth"/> or contains a
    /// cycle - the caller reports APG0012 and treats the parameter as unsupported.
    /// </param>
    public static ParameterModel? TryCreate(
        IParameterSymbol parameter,
        out EquatableArray<string> unsupportedConstraintAttributes,
        out ImmutableArray<ITypeSymbol> referencedObjectTypes,
        out ObjectGraphError? graphError)
    {
        var type = parameter.Type;

        if (string.Equals(GetMetadataName(type), CancellationTokenMetadataName, StringComparison.Ordinal))
        {
            unsupportedConstraintAttributes = EquatableArray<string>.Empty;
            referencedObjectTypes = ImmutableArray<ITypeSymbol>.Empty;
            graphError = null;
            return new ParameterModel(parameter.Name, ParameterShape.CancellationToken, Leaf: null, IsRequired: true, DefaultValueLiteral: null);
        }

        var isRequired = !parameter.HasExplicitDefaultValue;
        var defaultLiteral = parameter.HasExplicitDefaultValue
            ? RenderDefaultValueLiteral(parameter.ExplicitDefaultValue, type)
            : null;
        var description = ReadDescription(parameter.GetAttributes());

        if (TryGetArrayElementType(type, out var elementType, out var isConcreteArray))
        {
            var elementLeaf = TryCreateLeaf(elementType);

            if (elementLeaf is not null)
            {
                var arrayConstraints = ReadConstraints(parameter.GetAttributes(), ParameterShape.Array, elementLeaf.Kind, out unsupportedConstraintAttributes);
                referencedObjectTypes = ImmutableArray<ITypeSymbol>.Empty;
                graphError = null;
                return new ParameterModel(parameter.Name, ParameterShape.Array, elementLeaf, isRequired, defaultLiteral, isConcreteArray, description, arrayConstraints);
            }

            var state = new ObjectGraphState();
            var elementObject = TryCreateObjectType(elementType, depth: 0, state);
            unsupportedConstraintAttributes = CombineUnsupportedConstraintAttributes(parameter, state);
            referencedObjectTypes = state.ToReferencedTypes();
            graphError = state.Error;

            if (state.Error is not null || elementObject is null)
            {
                return null;
            }

            return new ParameterModel(parameter.Name, ParameterShape.ObjectArray, Leaf: null, isRequired, defaultLiteral, isConcreteArray, description, Constraints: null, Object: elementObject);
        }

        var leaf = TryCreateLeaf(type);

        if (leaf is not null)
        {
            var constraints = ReadConstraints(parameter.GetAttributes(), ParameterShape.Scalar, leaf.Kind, out unsupportedConstraintAttributes);
            referencedObjectTypes = ImmutableArray<ITypeSymbol>.Empty;
            graphError = null;
            return new ParameterModel(parameter.Name, ParameterShape.Scalar, leaf, isRequired, defaultLiteral, IsConcreteArray: false, Description: description, Constraints: constraints);
        }

        var rootState = new ObjectGraphState();
        var objectType = TryCreateObjectType(type, depth: 0, rootState);
        unsupportedConstraintAttributes = CombineUnsupportedConstraintAttributes(parameter, rootState);
        referencedObjectTypes = rootState.ToReferencedTypes();
        graphError = rootState.Error;

        if (rootState.Error is not null || objectType is null)
        {
            return null;
        }

        return new ParameterModel(parameter.Name, ParameterShape.Object, Leaf: null, isRequired, defaultLiteral, IsConcreteArray: false, Description: description, Constraints: null, Object: objectType);
    }

    /// <summary>Mutable, per-call state threaded through <see cref="TryCreateObjectType"/>/<see cref="TryCreateMember"/>.</summary>
    private sealed class ObjectGraphState
    {
        /// <summary>Ancestor types currently being walked - a repeat here is a cycle.</summary>
        public List<ITypeSymbol> Path { get; } = [];

        /// <summary>Every distinct object type seen so far, in first-seen order.</summary>
        public List<ITypeSymbol> ReferencedTypes { get; } = [];

        /// <summary>
        /// Every unsupported constraint attribute found on a MEMBER (never the root
        /// parameter itself - the caller reads the root's own directly), paired with the
        /// member's name so the reported diagnostic can name it.
        /// </summary>
        public List<(string MemberName, string AttributeName)> UnsupportedConstraintAttributes { get; } = [];

        public ObjectGraphError? Error { get; set; }

        public void AddReferenced(ITypeSymbol type)
        {
            foreach (var existing in ReferencedTypes)
            {
                if (SymbolEqualityComparer.Default.Equals(existing, type))
                {
                    return;
                }
            }

            ReferencedTypes.Add(type);
        }

        public ImmutableArray<ITypeSymbol> ToReferencedTypes() => ImmutableArray.CreateRange(ReferencedTypes);

        /// <summary>Renders each entry as <c>[Attribute] (on member 'Name')</c> for the shared APG0010 message template.</summary>
        public IEnumerable<string> RenderUnsupportedConstraintAttributes()
            => UnsupportedConstraintAttributes.Select(entry => $"{entry.AttributeName} (on member '{entry.MemberName}')");
    }

    /// <summary>
    /// Walks <paramref name="type"/> as a supported object type (135.1), recording every
    /// nested object it reaches into <paramref name="state"/>. Returns <see langword="null"/>
    /// both when <paramref name="type"/> is not a supported object shape (the caller reports
    /// APG0003) and when <see cref="ObjectGraphState.Error"/> is set (the caller reports
    /// APG0012) - the two are distinguished by whether <see cref="ObjectGraphState.Error"/>
    /// is populated.
    /// </summary>
    private static ObjectType? TryCreateObjectType(ITypeSymbol type, int depth, ObjectGraphState state)
    {
        for (var i = 0; i < state.Path.Count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(state.Path[i], type))
            {
                state.Error = new ObjectGraphError(ObjectGraphErrorKind.Cycle, PathText(state.Path, i, type));
                return null;
            }
        }

        if (depth > MaxObjectDepth)
        {
            state.Error = new ObjectGraphError(ObjectGraphErrorKind.DepthExceeded, PathText(state.Path, 0, type));
            return null;
        }

        if (!TryGetSinglePublicParameterizedConstructor(type, out var constructor))
        {
            return null;
        }

        state.AddReferenced(type);
        state.Path.Add(type);

        var members = ImmutableArray.CreateBuilder<ObjectMember>();
        var failed = false;

        foreach (var memberParameter in constructor.Parameters)
        {
            var member = TryCreateMember(memberParameter, depth + 1, state);

            if (state.Error is not null || member is null)
            {
                failed = true;
                break;
            }

            members.Add(member);
        }

        state.Path.RemoveAt(state.Path.Count - 1);

        if (failed)
        {
            return null;
        }

        var isNullable = type.NullableAnnotation == NullableAnnotation.Annotated;

        return new ObjectType(GetFullyQualifiedName(type), isNullable, members.ToImmutable());
    }

    /// <summary>
    /// Classifies one object member from its source constructor parameter (135.1: the
    /// single public constructor's parameters ARE the object's members - a positional
    /// record's primary constructor naturally supplies these). Mirrors the top-level
    /// scalar/array/object classification in <see cref="TryCreate"/>, minus the fields
    /// that only apply to a tool's own top-level parameter (135.1's <c>ObjectMember</c>
    /// carries no binding hint - a member is never bound on its own).
    /// </summary>
    private static ObjectMember? TryCreateMember(IParameterSymbol parameter, int depth, ObjectGraphState state)
    {
        var type = parameter.Type;
        var isRequired = !parameter.HasExplicitDefaultValue;
        var description = ReadDescription(parameter.GetAttributes());

        if (TryGetArrayElementType(type, out var elementType, out _))
        {
            var elementLeaf = TryCreateLeaf(elementType);

            if (elementLeaf is not null)
            {
                var constraints = ReadConstraints(parameter.GetAttributes(), ParameterShape.Array, elementLeaf.Kind, out var unsupported);
                foreach (var attributeName in unsupported)
                {
                    state.UnsupportedConstraintAttributes.Add((parameter.Name, attributeName));
                }

                return new ObjectMember(parameter.Name, parameter.Name, ParameterShape.Array, elementLeaf, Object: null, isRequired, description, constraints);
            }

            var elementObject = TryCreateObjectType(elementType, depth, state);

            if (state.Error is not null || elementObject is null)
            {
                return null;
            }

            foreach (var attributeName in ReadUnsupportedConstraintsForObjectShape(parameter.GetAttributes()))
            {
                state.UnsupportedConstraintAttributes.Add((parameter.Name, attributeName));
            }

            return new ObjectMember(parameter.Name, parameter.Name, ParameterShape.ObjectArray, Leaf: null, elementObject, isRequired, description, Constraints: null);
        }

        var leaf = TryCreateLeaf(type);

        if (leaf is not null)
        {
            var scalarConstraints = ReadConstraints(parameter.GetAttributes(), ParameterShape.Scalar, leaf.Kind, out var unsupportedScalar);

            foreach (var attributeName in unsupportedScalar)
            {
                state.UnsupportedConstraintAttributes.Add((parameter.Name, attributeName));
            }

            return new ObjectMember(parameter.Name, parameter.Name, ParameterShape.Scalar, leaf, Object: null, isRequired, description, scalarConstraints);
        }

        var objectType = TryCreateObjectType(type, depth, state);

        if (state.Error is not null || objectType is null)
        {
            return null;
        }

        foreach (var attributeName in ReadUnsupportedConstraintsForObjectShape(parameter.GetAttributes()))
        {
            state.UnsupportedConstraintAttributes.Add((parameter.Name, attributeName));
        }

        return new ObjectMember(parameter.Name, parameter.Name, ParameterShape.Object, Leaf: null, objectType, isRequired, description, Constraints: null);
    }

    /// <summary>
    /// A supported object type (135.1): public, non-generic, a reference type (<c>record</c>
    /// or <c>class</c> - never a <c>struct</c>/<c>record struct</c>), with EXACTLY ONE public
    /// constructor and at least one parameter. A positional record's compiler-generated copy
    /// constructor is <see langword="protected"/>, so it never competes with the primary
    /// constructor here.
    /// </summary>
    /// <remarks>
    /// A second shape - a parameterless constructor plus public
    /// <c>init</c>/<c>set</c> properties - for a type built through an object initializer
    /// instead of a positional record is not supported: every measured need
    /// (and every manual acceptance case) is the positional-record shape, and the property
    /// path has no reliable, unmeasured signal for which properties are required versus
    /// optional (a constructor parameter has <see cref="IParameterSymbol.HasExplicitDefaultValue"/>;
    /// a property has nothing equivalent). Recorded as a scope reduction, not a silent gap.
    /// </remarks>
    private static bool TryGetSinglePublicParameterizedConstructor(ITypeSymbol type, out IMethodSymbol constructor)
    {
        constructor = null!;

        if (type is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Class,
                IsAbstract: false,
                IsStatic: false,
                IsGenericType: false,
                DeclaredAccessibility: Accessibility.Public,
            } named)
        {
            return false;
        }

        IMethodSymbol? candidate = null;
        var publicConstructorCount = 0;

        foreach (var ctor in named.Constructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public || ctor.IsStatic)
            {
                continue;
            }

            publicConstructorCount++;
            candidate = ctor;
        }

        if (publicConstructorCount != 1 || candidate is not { Parameters.Length: > 0 })
        {
            return false;
        }

        constructor = candidate;
        return true;
    }

    /// <summary>Renders an ancestor chain as <c>A → B → A</c> for an APG0012 message.</summary>
    private static string PathText(List<ITypeSymbol> path, int fromIndex, ITypeSymbol closingType)
    {
        var names = new List<string>();

        for (var i = fromIndex; i < path.Count; i++)
        {
            names.Add(path[i].Name);
        }

        names.Add(closingType.Name);

        return string.Join(" → ", names);
    }

    /// <summary>
    /// Every <see cref="System.ComponentModel.DataAnnotations"/> constraint attribute this
    /// validator knows applies only to a scalar or scalar array (130.2) - none of them has a
    /// meaning for an object or object array. Reports each one found as unsupported (APG0010)
    /// instead of silently ignoring it, matching 130's "never silently produce a wrong schema"
    /// rule for every other unrenderable case.
    /// </summary>
    /// <summary>
    /// Combines the root object/object-array parameter's OWN unsupported constraint
    /// attributes with every one found on a member anywhere in its graph
    /// (<see cref="ObjectGraphState.UnsupportedConstraintAttributes"/>) into the single
    /// list the caller reports as APG0010. Without this, a mismatched constraint on a
    /// nested member (<c>[Range]</c> on an object member's <c>string</c>) is recorded
    /// during the walk but never reaches the caller - <see cref="TryCreateMember"/>
    /// populates <see cref="ObjectGraphState"/>, not the method's own <see langword="out"/>
    /// parameter.
    /// </summary>
    private static EquatableArray<string> CombineUnsupportedConstraintAttributes(IParameterSymbol parameter, ObjectGraphState state)
    {
        var combined = new List<string>(ReadUnsupportedConstraintsForObjectShape(parameter.GetAttributes()));
        combined.AddRange(state.RenderUnsupportedConstraintAttributes());

        return combined.Count == 0 ? EquatableArray<string>.Empty : ImmutableArray.CreateRange(combined);
    }

    private static EquatableArray<string> ReadUnsupportedConstraintsForObjectShape(IEnumerable<AttributeData> attributes)
    {
        var unsupported = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            var metadataName = $"{attributeClass.ContainingNamespace}.{attributeClass.Name}";

            var displayName = metadataName switch
            {
                RangeAttributeMetadataName => "[Range]",
                MinLengthAttributeMetadataName => "[MinLength]",
                MaxLengthAttributeMetadataName => "[MaxLength]",
                StringLengthAttributeMetadataName => "[StringLength]",
                RegularExpressionAttributeMetadataName => "[RegularExpression]",
                _ => null,
            };

            if (displayName is not null)
            {
                unsupported.Add(displayName);
            }
        }

        return unsupported.Count == 0 ? EquatableArray<string>.Empty : unsupported.ToImmutable();
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
        IEnumerable<AttributeData> attributes,
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
        var unsupported = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in attributes)
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

        unsupportedAttributes = unsupported.Count == 0 ? EquatableArray<string>.Empty : unsupported.ToImmutable();

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
    private static string? ReadDescription(IEnumerable<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
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

            return LeafType.Enum(GetFullyQualifiedName(effectiveType), isNullable, ImmutableArray.Create(memberNames));
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
