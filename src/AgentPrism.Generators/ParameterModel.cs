namespace AgentPrism.Generators;

/// <summary>The binding shape of a parameter.</summary>
internal enum ParameterShape
{
    /// <summary>One value converted from a JSON argument.</summary>
    Scalar,

    /// <summary>An array or list whose elements convert to the same leaf type.</summary>
    Array,

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

/// <summary>The generator model for one method parameter.</summary>
/// <param name="IsConcreteArray">
/// For <see cref="ParameterShape.Array"/>, identifies whether the C# parameter type
/// is a bare array, <c>T[]</c>, or an interface such as <c>IReadOnlyList&lt;T&gt;</c>.
/// The run-time helper, <c>AgentPrismGeneratedToolArguments.GetArray</c>, always
/// returns <c>IReadOnlyList&lt;T&gt;</c>. When the target is <c>T[]</c>, <c>SourceWriter</c>
/// uses this field to add <c>.ToArray()</c>; otherwise, compilation fails with CS1503.
/// </param>
internal sealed record ParameterModel(
    string Name,
    ParameterShape Shape,
    LeafType? Leaf,
    bool IsRequired,
    string? DefaultValueLiteral,
    bool IsConcreteArray = false);
