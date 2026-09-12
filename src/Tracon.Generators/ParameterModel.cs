namespace Tracon.Generators;

/// <summary>The binding shape of a parameter.</summary>
internal enum ParameterShape
{
    /// <summary>One value converted from a JSON argument.</summary>
    Scalar,

    /// <summary>An array or list whose elements convert to the same leaf type.</summary>
    Array,

    /// <summary>A supported object type (135.1), deserialized through a tool-owned <c>JsonSerializerContext</c>.</summary>
    Object,

    /// <summary>An array or list whose elements are a supported object type.</summary>
    ObjectArray,

    /// <summary><see cref="System.Threading.CancellationToken"/>, omitted from the schema.</summary>
    CancellationToken,
}

/// <summary>
/// The leaf CLR type of a parameter or an array parameter element. JSON schema and
/// converter expression derive from this information. Under 52.5, generated code
/// must only be a function of these fields. It does not retain a separate converter
/// text field because the two representations could diverge.
/// </summary>
internal sealed record LeafType(LeafTypeKind Kind, string ClrTypeDisplay, bool IsNullable, EquatableArray<string> EnumMemberNames)
{
    public static LeafType Scalar(LeafTypeKind kind, string clrTypeDisplay, bool isNullable)
        => new(kind, clrTypeDisplay, isNullable, EquatableArray<string>.Empty);

    public static LeafType Enum(string clrTypeDisplay, bool isNullable, EquatableArray<string> memberNames)
        => new(LeafTypeKind.Enum, clrTypeDisplay, isNullable, memberNames);
}

/// <summary>A supported leaf type family.</summary>
internal enum LeafTypeKind
{
    Boolean,
    Integer,
    Number,
    String,
    Guid,
    DateTime,
    DateTimeOffset,
    Enum,
}

/// <summary>
/// A JSON Schema constraint set read from standard <c>System.ComponentModel.DataAnnotations</c>
/// attributes (130.1). Every field is a primitive value: this record participates in the
/// incremental generator's value equality (52.5), and a non-primitive field would fall back
/// to reference equality and defeat the pipeline's cache (130.4).
/// </summary>
/// <param name="Minimum">
/// From <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/>, rendered with
/// <see cref="System.Globalization.CultureInfo.InvariantCulture"/> and kept as text - an
/// intermediate numeric type risks rounding the source literal.
/// </param>
internal sealed record ParameterConstraints(
    string? Minimum,
    string? Maximum,
    int? MinLength,
    int? MaxLength,
    int? MinItems,
    int? MaxItems,
    string? Pattern)
{
    /// <summary>No constraint was read for this parameter.</summary>
    public static readonly ParameterConstraints None = new(null, null, null, null, null, null, null);

    /// <summary><see langword="true"/> when no field carries a constraint.</summary>
    public bool IsEmpty =>
        Minimum is null && Maximum is null &&
        MinLength is null && MaxLength is null &&
        MinItems is null && MaxItems is null &&
        Pattern is null;
}

/// <summary>
/// One member of a supported object type's single public constructor (135.1). Mirrors
/// <see cref="ParameterModel"/>'s shape fields, minus the pieces that only make sense for a
/// tool's own top-level parameter (binding hints such as <c>IsConcreteArray</c> and
/// <c>DefaultValueLiteral</c>): a member is never bound on its own, only as part of the
/// object it belongs to - <c>System.Text.Json</c> deserializes the whole object in one call
/// through the tool owner's <c>JsonSerializerContext</c> (135.5).
/// </summary>
/// <param name="JsonName">
/// The JSON key this member is read from. Equal to <paramref name="Name"/> today - the
/// generator does not rename a member to <c>camelCase</c>: undecided without measurement,
/// so the member's declared name is kept verbatim, matching the existing top-level
/// parameter convention.
/// </param>
internal sealed record ObjectMember(
    string Name,
    string JsonName,
    ParameterShape Shape,
    LeafType? Leaf,
    ObjectType? Object,
    bool IsRequired,
    string? Description,
    ParameterConstraints? Constraints);

/// <summary>
/// A supported object-shaped parameter or member type (135.1): a public, non-generic
/// record or class with a single public, parameterized constructor. Carries no Roslyn
/// symbol - only <see langword="string"/> and value-equality fields, so it participates
/// correctly in the incremental generator's cache (135.3).
/// </summary>
internal sealed record ObjectType(
    string ClrTypeDisplay,
    bool IsNullable,
    EquatableArray<ObjectMember> Members);

/// <summary>The generator model for one method parameter.</summary>
/// <param name="IsConcreteArray">
/// For <see cref="ParameterShape.Array"/> and <see cref="ParameterShape.ObjectArray"/>,
/// identifies whether the C# parameter type is a bare array, <c>T[]</c>, or an interface
/// such as <c>IReadOnlyList&lt;T&gt;</c>. The run-time helper,
/// <c>TraconGeneratedToolArguments.GetArray</c>, always returns
/// <c>IReadOnlyList&lt;T&gt;</c>. When the target is <c>T[]</c>, <c>SourceWriter</c>
/// uses this field to add <c>.ToArray()</c>; otherwise, compilation fails with CS1503.
/// </param>
/// <param name="Constraints">
/// JSON Schema constraints (130.1). <see cref="ParameterConstraints.MinItems"/>/
/// <see cref="ParameterConstraints.MaxItems"/> belong on the array node; every other
/// field belongs on the leaf node, which is also the array's <c>items</c> node when
/// <paramref name="Shape"/> is <see cref="ParameterShape.Array"/>.
/// </param>
/// <param name="Object">
/// Populated when <paramref name="Shape"/> is <see cref="ParameterShape.Object"/> (the
/// parameter's own type) or <see cref="ParameterShape.ObjectArray"/> (the array's element
/// type) - 135.1.
/// </param>
internal sealed record ParameterModel(
    string Name,
    ParameterShape Shape,
    LeafType? Leaf,
    bool IsRequired,
    string? DefaultValueLiteral,
    bool IsConcreteArray = false,
    string? Description = null,
    ParameterConstraints? Constraints = null,
    ObjectType? Object = null);
