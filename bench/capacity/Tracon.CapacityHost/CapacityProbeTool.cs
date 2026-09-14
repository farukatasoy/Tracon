using Microsoft.Extensions.AI;
using Tracon.Capacity;

namespace Tracon.CapacityHost;

/// <summary>The code tool the agent calls once per run.</summary>
/// <remarks>
/// <para>
/// 🚨 Its dependencies are captured AT REGISTRATION TIME, not resolved from the
/// function's own arguments: Microsoft Agent Framework hands a tool an EMPTY
/// service provider (K-218), so anything resolved inside the body would be
/// null under load and the failure would look like a capacity limit.
/// </para>
/// <para>
/// The tool carries the run's correlation value through its argument and back
/// through its result. That is what makes "the tool ran once, for THIS run, and
/// its result reached the model" a measurement rather than an assumption.
/// </para>
/// </remarks>
public sealed class CapacityProbeTool
{
    private readonly HostWorkload _workload;
    private readonly CapacityCounters _counters;

    /// <summary>Creates the tool with the dependencies it will need at call time.</summary>
    /// <param name="workload">The synthetic workload.</param>
    /// <param name="counters">The host's counters.</param>
    public CapacityProbeTool(HostWorkload workload, CapacityCounters counters)
    {
        _workload = workload;
        _counters = counters;
    }

    /// <summary>Builds the function the host registers.</summary>
    /// <returns>The function.</returns>
    public AIFunction CreateFunction()
        => AIFunctionFactory.Create(
            (Func<string, string>)Probe,
            CapacityPayload.ToolName,
            "Returns a deterministic probe result for the given correlation value.");

    private string Probe(string correlation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlation);

        _counters.ToolInvocation();
        CapacityRunLedger.RecordToolCall(correlation);

        return CapacityPayload.ToolResult(correlation, _workload.ToolResultBytes);
    }
}
