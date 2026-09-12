using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Reports what a <c>runs</c> row records.</summary>
/// <remarks>
/// <para>
/// A separate table is <strong>not opened</strong> for workflow runs. The
/// runs screen, the SSE stream, tenant filters, statistics, and the waterfall
/// are already built on top of <c>runs</c>; a second recording path would
/// double all of them. The distinction is made through this column instead.
/// </para>
/// <para>
/// Written <strong>as a name</strong> in JSON; stored as <c>smallint</c> in
/// the database. The value order must not change.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunKind>))]
public enum RunKind
{
    /// <summary>The run of a single agent.</summary>
    Agent = 0,

    /// <summary>
    /// The run of a workflow. Every agent called inside it is linked under
    /// this row through the <c>parent_run_id</c> mechanism.
    /// </summary>
    Workflow = 1,

    /// <summary>
    /// The run of an eval case. A normal <c>runs</c> row for
    /// transcript and span-tree access, but
    /// <see cref="IRunStore.GetStatisticsAsync"/> excludes this kind from the
    /// summary — it is a synthetic test call, not real traffic.
    /// </summary>
    Eval = 2,
}
