using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>The reason an <see cref="EvalCase"/> was promoted from a production run.</summary>
/// <remarks>
/// Written as a name in JSON, stored as <c>smallint</c> in the database. The
/// value order <strong>must not change</strong> — only append.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EvalCaseSource>))]
public enum EvalCaseSource
{
    /// <summary>Promoted from a failed (<see cref="RunStatus.Failed"/>) run.</summary>
    FailedRun = 0,

    /// <summary>Promoted from a negatively scored run.</summary>
    NegativeScore = 1,

    /// <summary>Promoted from a successful run, as a reference.</summary>
    ReferenceRun = 2,
}
