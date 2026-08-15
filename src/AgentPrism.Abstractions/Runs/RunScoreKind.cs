using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The shape of the value a <see cref="RunScore"/> carries.</summary>
/// <remarks>
/// Written as a name in JSON; stored as <c>smallint</c> in the database. The
/// numeric values are <strong>stable</strong> and must not change.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunScoreKind>))]
public enum RunScoreKind
{
    /// <summary>A binary score: <see cref="RunScore.Value"/> is 0 (negative) or 1 (positive).</summary>
    Binary = 1,

    /// <summary>A star rating: <see cref="RunScore.Value"/> is between 1 and 5.</summary>
    Stars = 2,

    /// <summary>
    /// A 0-100 integer percentage score. Produced by the model-based judge (Phase 49).
    /// </summary>
    Numeric = 3,
}
