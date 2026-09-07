using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The shape of the value a <see cref="RunScore"/> carries.</summary>
/// <remarks>
/// <para>
/// Written as a name in JSON; stored as <c>smallint</c> in the database. The
/// numeric values are <strong>stable</strong> and must not change.
/// </para>
/// <para>
/// The three shapes line up with the three metric shapes of
/// <c>Microsoft.Extensions.AI.Evaluation</c>: <c>BooleanMetric</c> maps to
/// <see cref="Binary"/>, <c>NumericMetric</c> to <see cref="Stars"/> and
/// <see cref="Numeric"/>, and <c>StringMetric</c> to <see cref="Categorical"/>.
/// Only the shape is aligned; no metric type is stored.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunScoreKind>))]
public enum RunScoreKind
{
    /// <summary>A binary score: <see cref="RunScore.Value"/> is 0 (negative) or 1 (positive).</summary>
    Binary = 1,

    /// <summary>A star rating: <see cref="RunScore.Value"/> is between 1 and 5.</summary>
    Stars = 2,

    /// <summary>
    /// A 0-100 percentage score. Produced by the model-based judge.
    /// </summary>
    Numeric = 3,

    /// <summary>
    /// A categorical score: the value is a short label carried by
    /// <see cref="RunScore.TextValue"/>, and <see cref="RunScore.Value"/> is
    /// <see langword="null"/>.
    /// </summary>
    Categorical = 4,
}
