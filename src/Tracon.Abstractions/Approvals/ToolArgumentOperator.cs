using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// A comparison operator for a <see cref="ToolArgumentCondition"/>.
/// </summary>
/// <remarks>
/// <see cref="JsonStringEnumConverter{TEnum}"/> is required: ASP.NET Core's default JSON serialization writes an unmarked enum as a number, not the string a client (and this type's own HTTP contract, <see cref="ToolArgumentCondition"/>) expects.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ToolArgumentOperator>))]
public enum ToolArgumentOperator
{
    /// <summary>The argument equals the value. Accepts text, number, or boolean.</summary>
    Equals = 0,

    /// <summary>The argument does not equal the value. Accepts text, number, or boolean.</summary>
    NotEquals = 1,

    /// <summary>The argument is greater than the value. Accepts a number only.</summary>
    GreaterThan = 2,

    /// <summary>The argument is greater than or equal to the value. Accepts a number only.</summary>
    GreaterThanOrEqual = 3,

    /// <summary>The argument is less than the value. Accepts a number only.</summary>
    LessThan = 4,

    /// <summary>The argument is less than or equal to the value. Accepts a number only.</summary>
    LessThanOrEqual = 5,

    /// <summary>The argument is a member of the value, a JSON array of text or numbers.</summary>
    In = 6,

    /// <summary>The argument is not a member of the value, a JSON array of text or numbers.</summary>
    NotIn = 7,
}
