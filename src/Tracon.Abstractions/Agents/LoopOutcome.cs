using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// What a harness loop does when it ends without every criterion satisfied: return
/// the last response, or fail the run.
/// </summary>
/// <remarks>
/// <para>
/// Read by <see cref="LoopSettings.OnExhausted"/> (the iteration ceiling is reached
/// while a criterion still asks for another iteration) and
/// <see cref="LoopSettings.OnCriterionError"/> (a criterion throws while it is
/// evaluated). The two are set independently: "the judge could not be reached,
/// keep the answer" and "the judge said no, stop" are different needs.
/// </para>
/// <para>
/// Written <strong>as a name</strong> in JSON (<c>"FailRun"</c>), like every other
/// definition enum. A numeric value that is not a member fails the definition when it
/// is compiled.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LoopOutcome>))]
public enum LoopOutcome
{
    /// <summary>
    /// The loop stops and the run returns the last response as a success. The default,
    /// and the behavior before these settings existed.
    /// </summary>
    ReturnLastResponse = 0,

    /// <summary>
    /// The run fails. A workflow that runs the agent stops at that node, and a caller
    /// gets the error instead of a response no criterion accepted.
    /// </summary>
    FailRun = 1,
}
