namespace Tracon;

/// <summary>Names the activity sources other than Tracon's own that the run trace listens to.</summary>
internal static class TraconTelemetrySources
{
    /// <summary>
    /// The activity source of the Microsoft Agent Framework workflow telemetry, used when a consumer calls
    /// <c>WorkflowBuilder.WithOpenTelemetry()</c> on a graph built in code and passes no source of their own.
    /// </summary>
    /// <remarks>
    /// A test compares this value with the name the framework really emits, so a rename in a framework upgrade fails
    /// the build instead of silently blinding the listener.
    /// </remarks>
    public const string MafWorkflows = "Microsoft.Agents.AI.Workflows";
}
