namespace Tracon;

/// <summary>
/// The Tracon schema generation each kind of persisted state is written
/// with by THIS build.
/// </summary>
/// <remarks>
/// <para>
/// A generation advances only when Tracon changes how it structures the
/// stored row, never when the Microsoft Agent Framework version changes.
/// </para>
/// <para>
/// <strong>Deliberately not public.</strong> A consumer never needs to compare
/// against these numbers directly — <see cref="SessionRecord.StateSchemaVersion"/>
/// and <see cref="WorkflowCheckpointRecord.StateSchemaVersion"/> already say
/// which generation their own row carries, and <c>StatePreflight</c> turns the
/// comparison into a report. Publishing the constant would turn an
/// implementation detail into a promise that cannot be changed without a
/// breaking release.
/// </para>
/// <para>
/// They live here, in one place, rather than beside each writer: two copies of
/// the same number in two assemblies drift the moment only one of them is
/// bumped, and the preflight has to read both.
/// </para>
/// </remarks>
internal static class StateSchemaGenerations
{
    /// <summary>The generation <c>AgentSessionManager</c> stamps onto every session it saves.</summary>
    internal const int Session = 1;

    /// <summary>The generation <c>TraconCheckpointStore</c> stamps onto every checkpoint it writes.</summary>
    internal const int WorkflowCheckpoint = 1;
}
