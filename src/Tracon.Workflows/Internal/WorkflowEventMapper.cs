using System.Globalization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Converts Microsoft Agent Framework workflow events into Tracon event drafts.
/// </summary>
/// <remarks>
/// <strong>Branch order matters.</strong> <c>AgentResponseEvent</c> and
/// <c>AgentResponseUpdateEvent</c> <em>derive from</em> <c>WorkflowOutputEvent</c>
/// (). If the general branch is matched first, agent
/// responses are classified as "the workflow produced output" and the real
/// output is lost.
/// </remarks>
internal static class WorkflowEventMapper
{
    /// <summary>Converts a single MAF event into an Tracon event draft.</summary>
    /// <param name="workflowEvent">The event to convert.</param>
    /// <returns>The conversion result.</returns>
    public static WorkflowEventMapping Map(WorkflowEvent workflowEvent)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);

        return workflowEvent switch
        {
            WorkflowStartedEvent => new RunEventDraft(RunEventType.WorkflowStarted),

            // Agent responses are matched FIRST because they DERIVE FROM WorkflowOutputEvent.
            AgentResponseUpdateEvent update => new RunEventDraft(RunEventType.MessageDelta)
            {
                Text = update.Update?.Text,
                ToolName = update.ExecutorId,
            },

            // In a streaming run the update events already carry the text; writing
            // the full response event too would show the same text twice in the stream.
            AgentResponseEvent => WorkflowEventMapping.Skipped,

            SuperStepStartedEvent started => new RunEventDraft(RunEventType.SuperStepStarted)
            {
                Text = started.StepNumber.ToString(CultureInfo.InvariantCulture),
                Payload = Join(started.StartInfo?.SendingExecutors),
            },

            SuperStepCompletedEvent completed => new RunEventDraft(RunEventType.SuperStepCompleted)
            {
                Text = completed.StepNumber.ToString(CultureInfo.InvariantCulture),
                Payload = DescribeCompletion(completed),
            },

            ExecutorFailedEvent failed => new RunEventDraft(RunEventType.ExecutorFailed)
            {
                Text = failed.ExecutorId,
                Payload = failed.Data?.Message,
            },

            ExecutorInvokedEvent invoked => new RunEventDraft(RunEventType.ExecutorInvoked)
            {
                Text = invoked.ExecutorId,
            },

            ExecutorCompletedEvent completed => new RunEventDraft(RunEventType.ExecutorCompleted)
            {
                Text = completed.ExecutorId,
            },

            // The payload is enough to show the request in the UI: port, types,
            // text, and which input field is being asked for. Pending requests
            // are read from this event; there is no separate table (phase 16).
            RequestInfoEvent request => new RunEventDraft(RunEventType.WorkflowRequest)
            {
                Text = request.Request.RequestId,
                Payload = WorkflowRequestDescriptor.Describe(request.Request),
            },

            WorkflowErrorEvent error => new RunEventDraft(RunEventType.ExecutorFailed)
            {
                Text = error.Exception?.GetType().Name,
                Payload = error.Exception?.Message,
            },

            WorkflowWarningEvent warning => new RunEventDraft(RunEventType.SuperStepCompleted)
            {
                Text = "warning",
                Payload = warning.Data?.ToString(),
            },

            WorkflowOutputEvent output => new RunEventDraft(RunEventType.WorkflowOutput)
            {
                Text = DescribeOutput(output),
            },

            _ => WorkflowEventMapping.Unknown,
        };
    }

    /// <summary>Extracts the text carried by an output event.</summary>
    /// <param name="output">The output event.</param>
    /// <returns>Readable text; the type name if none can be extracted.</returns>
    /// <remarks>
    /// Every ready-made pattern produces a <c>List&lt;ChatMessage&gt;</c>
    /// (). A free-form graph defined in code may return a
    /// different type; in that case only the type name is written - dumping an
    /// unknown payload into the event table with <c>ToString()</c> would bloat
    /// the table.
    /// </remarks>
    public static string? DescribeOutput(WorkflowOutputEvent output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var data = output.Data;

        return data switch
        {
            null => null,
            string text => text,
            IEnumerable<ChatMessage> messages => string.Join(
                Environment.NewLine,
                messages
                    .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
                    .Select(static message => message.Text)),
            ChatMessage message => message.Text,
            _ => data.GetType().Name,
        };
    }

    private static string DescribeCompletion(SuperStepCompletedEvent completed)
    {
        var activated = Join(completed.CompletionInfo?.ActivatedExecutors);
        var checkpoint = completed.CompletionInfo?.Checkpoint?.CheckpointId;

        return checkpoint is null ? activated : $"{activated} · checkpoint={checkpoint}";
    }

    private static string Join(IEnumerable<string>? values)
        => values is null
            ? string.Empty
            : string.Join(", ", values.OrderBy(static value => value, StringComparer.Ordinal));
}

/// <summary>The conversion result of a workflow event.</summary>
/// <param name="Draft">The event draft to write. <see langword="null"/> if the event was skipped.</param>
/// <param name="IsKnown">
/// Whether the event type is recognized. An event where this is
/// <see langword="false"/> is <strong>not dropped silently</strong>; the
/// caller logs it: if Microsoft Agent Framework adds a new event type one day,
/// missing that would make debugging impossible.
/// </param>
internal readonly record struct WorkflowEventMapping(RunEventDraft? Draft, bool IsKnown)
{
    /// <summary>A recognized event that is not written to the event stream.</summary>
    public static WorkflowEventMapping Skipped { get; } = new(null, IsKnown: true);

    /// <summary>An unrecognized event.</summary>
    public static WorkflowEventMapping Unknown { get; } = new(null, IsKnown: false);

    /// <summary>Converts a draft into a recognized conversion result.</summary>
    /// <param name="draft">The event draft.</param>
    public static implicit operator WorkflowEventMapping(RunEventDraft draft) => new(draft, IsKnown: true);
}
