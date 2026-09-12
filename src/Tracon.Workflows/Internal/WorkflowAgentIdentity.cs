using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Gives the agent wrapper bound to a workflow a <strong>permanent</strong>
/// identity, so checkpoints remain valid even after the process restarts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is needed.</strong> Microsoft Agent Framework derives
/// executor ids from the agent <em>instance</em>: the id has the form
/// <c>{Name}_{AIAgent.Id}</c>, and <c>AIAgent.Id</c> is generated randomly for
/// each instance. An in-process cache pins it
/// (<see cref="WorkflowAgentCache"/>), but the cache lives in process memory:
/// when the application restarts, the ids change and MAF rejects the old
/// checkpoint with <c>InvalidDataException</c>. A run awaiting human input was
/// therefore lost on every deployment.
/// </para>
/// <para>
/// <strong>The agent executor is the only thing in the graph whose identity
/// varies.</strong>: every other executor id produced by
/// the ready-made patterns (<c>OutputMessages</c>, <c>Start</c>,
/// <c>Batcher/*</c>, <c>ConcurrentEnd</c>, <c>HandoffStart</c>,
/// <c>HandoffEnd</c>, <c>GroupChatHost</c>, <c>MagenticOrchestrator</c>) is
/// already fixed. So pinning only <c>AIAgent.Id</c> gives
/// <strong>all five patterns</strong> a permanent identity, with no need to
/// build the graph by hand.
/// </para>
/// <para>
/// <strong>The identity is written to a private field.</strong>
/// <c>AIAgent.Id</c> is not virtual and not writable; a derived class cannot
/// override it (verified via reflection). The only way is to write the base
/// class's auto-property backing field (<c>&lt;Id&gt;k__BackingField</c>). The
/// write happens <strong>only on Tracon's own wrapper instance</strong>;
/// MAF's own objects are never touched. If MAF removes this field, the
/// identity stays random and behavior reverts to the in-process form: the warning message
/// already says what to do. Silent breakage is caught by
/// <c>WorkflowAgentIdentityTests</c> - the test fails if the identity does not
/// carry the expected value.
/// </para>
/// </remarks>
internal static class WorkflowAgentIdentity
{
    /// <summary>The field name the compiler generates for the <c>AIAgent.Id</c> auto-property.</summary>
    private const string BackingFieldName = "<Id>k__BackingField";

    private static readonly FieldInfo? IdField = typeof(AIAgent)
        .GetField(BackingFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Gets whether a permanent identity can be assigned.</summary>
    /// <remarks>
    /// When <see langword="false"/>, Microsoft Agent Framework has changed its
    /// <c>AIAgent.Id</c> implementation. Execution still works; only old
    /// checkpoints become unusable once the process restarts.
    /// </remarks>
    public static bool IsSupported => IdField is not null;

    /// <summary>
    /// Computes a permanent identity for a <c>(workflow, agent)</c> pair.
    /// </summary>
    /// <param name="workflowName">The name of the enclosing workflow.</param>
    /// <param name="agentName">The name of the bound agent.</param>
    /// <returns>A 32-character hexadecimal identity.</returns>
    /// <remarks>
    /// The format matches <c>Guid.ToString("n")</c>. This is deliberate: the id
    /// MAF generates has the same format, and the executor id is composed as
    /// <c>{name}_{id}</c>. Carrying a separator character (<c>:</c>,
    /// <c>/</c>) inside the name or id would needlessly put Mermaid node names
    /// and checkpoint keys at risk.
    /// </remarks>
    public static string Compute(string workflowName, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        // '\n' is used as the separator: it cannot appear in workflow or agent
        // names, so ("a-b", "c") and ("a", "b-c") cannot produce the same identity.
        var seed = $"tracon/workflow\n{workflowName}\n{agentName}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        // 16 bytes is as wide as a GUID; the collision probability is negligible.
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the permanent identity onto the wrapper. If it cannot be
    /// written, warns once and continues with a random identity.
    /// </summary>
    /// <param name="agent">The wrapper whose identity should be pinned.</param>
    /// <param name="workflowName">The name of the enclosing workflow.</param>
    /// <param name="agentName">The name of the bound agent.</param>
    /// <param name="logger">The logger.</param>
    /// <returns><see langword="true"/> if the identity was written.</returns>
    /// <remarks>
    /// Failure <strong>does not throw</strong>. A permanent identity is an
    /// enhancement; without it, workflows still run and only
    /// resuming after a restart is lost. Stopping all workflow execution over
    /// an internal MAF change would be a disproportionate penalty.
    /// </remarks>
    public static bool TryApply(AIAgent agent, string workflowName, string agentName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(logger);

        if (IdField is null)
        {
            logger.LogWarning(
                "Could not write a permanent executor identity for workflow agent '{Agent}': " +
                "Microsoft Agent Framework no longer has the '{Field}' field. " +
                "Execution runs normally; but old checkpoints become unusable " +
                "once the application restarts.",
                agentName,
                BackingFieldName);

            return false;
        }

        IdField.SetValue(agent, Compute(workflowName, agentName));

        return true;
    }
}
