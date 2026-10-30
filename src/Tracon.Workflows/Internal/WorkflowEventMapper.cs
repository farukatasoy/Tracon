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
    /// <summary>Converts a single MAF event into a Tracon event draft.</summary>
    /// <param name="workflowEvent">The event to convert.</param>
    /// <param name="failureText">
    /// The text a failure event carries: the display-safe text the runner
    /// logged the exception under (<c>SafeErrorText</c>), never the
    /// exception's own message. Ignored for every other event.
    /// </param>
    /// <param name="maxPayloadLength">
    /// The writer's <c>MaxPayloadLength</c>, which an unrecognized event's payload is fitted to.
    /// </param>
    /// <param name="presentation">
    /// How a <see cref="IToolApprovalPresenter"/> shows a tool approval request; ignored for every other event.
    /// </param>
    /// <param name="agentName">The agent that asked for a tool approval; ignored for every other event.</param>
    /// <returns>The conversion result.</returns>
    public static WorkflowEventMapping Map(
        WorkflowEvent workflowEvent,
        string? failureText = null,
        int maxPayloadLength = 0,
        ToolApprovalPresentation? presentation = null,
        string? agentName = null)
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

            // The exception's own message can carry a host:port
            // or a request detail its producer put there; the runner passes
            // the safe text it logged instead.
            ExecutorFailedEvent failed => new RunEventDraft(RunEventType.ExecutorFailed)
            {
                Text = failed.ExecutorId,
                Payload = failureText,
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
            // are read from this event; there is no separate table.
            RequestInfoEvent request => new RunEventDraft(RunEventType.WorkflowRequest)
            {
                Text = request.Request.RequestId,
                Payload = WorkflowRequestDescriptor.Describe(request.Request, presentation, agentName),
            },

            WorkflowErrorEvent error => new RunEventDraft(RunEventType.ExecutorFailed)
            {
                Text = error.Exception?.GetType().Name,
                Payload = failureText,
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

            // An event Tracon has no type for is RECORDED, not dropped. A code
            // workflow can raise its own WorkflowEvent, and a later MAF can add a type;
            // either way the run record must show it happened.
            _ => new WorkflowEventMapping(DescribeUnrecognized(workflowEvent, maxPayloadLength), IsKnown: false),
        };
    }

    /// <summary>
    /// Describes an event Tracon does not recognize as a reserved
    /// <see cref="RunEventCustomTypes.WorkflowEvent"/> draft.
    /// </summary>
    /// <param name="workflowEvent">The unrecognized event.</param>
    /// <param name="maxPayloadLength">
    /// The writer's <c>MaxPayloadLength</c>, or zero or less for none; the text data is cut so the
    /// whole payload fits it.
    /// </param>
    /// <returns>The draft to append with the writer's reserved path.</returns>
    /// <remarks>
    /// <c>Text</c> is the CLR type name. The payload names the full type, the executor when
    /// the event derives from the framework's executor event, and the data by the same rule as
    /// an output event: text for a string or chat message, the type name for anything else.
    /// Never <c>ToString()</c>: an arbitrary payload object would bloat the event table or put a
    /// field in it that was never meant to be stored.
    /// </remarks>
    public static RunEventDraft DescribeUnrecognized(WorkflowEvent workflowEvent, int maxPayloadLength = 0)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);

        var type = workflowEvent.GetType();
        var executorId = (workflowEvent as ExecutorEvent)?.ExecutorId;

        var payload = ContentEventPayload.BuildFitting(
            maxPayloadLength,
            DescribeData(workflowEvent.Data),
            data => ContentEventPayload.Build(writer =>
            {
                ContentEventPayload.WriteString(writer, "eventType", type.FullName ?? type.Name);

                if (executorId is not null)
                {
                    ContentEventPayload.WriteString(writer, "executorId", executorId);
                }

                ContentEventPayload.WriteString(writer, "data", data);
            }));

        return new RunEventDraft(RunEventType.Custom)
        {
            CustomType = RunEventCustomTypes.WorkflowEvent,
            Text = type.Name,
            Payload = payload,
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

        return DescribeData(output.Data);
    }

    private static string? DescribeData(object? data)
    {
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
/// <see langword="false"/> is <strong>not dropped silently</strong>: its draft is a
/// reserved <see cref="RunEventCustomTypes.WorkflowEvent"/> event, which the caller
/// appends with the writer's reserved path. If Microsoft Agent Framework adds a new
/// event type one day, missing that would make debugging impossible.
/// </param>
internal readonly record struct WorkflowEventMapping(RunEventDraft? Draft, bool IsKnown)
{
    /// <summary>A recognized event that is not written to the event stream.</summary>
    public static WorkflowEventMapping Skipped { get; } = new(null, IsKnown: true);

    /// <summary>Converts a draft into a recognized conversion result.</summary>
    /// <param name="draft">The event draft.</param>
    public static implicit operator WorkflowEventMapping(RunEventDraft draft) => new(draft, IsKnown: true);
}
