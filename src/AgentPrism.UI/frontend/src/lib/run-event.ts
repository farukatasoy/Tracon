/**
 * The shape of one run event, as it arrives over the `text/event-stream`
 * `update`/`run` SSE frames.
 *
 * Deliberately hand-written, not generated: an SSE payload is not a JSON HTTP
 * response body, so it carries no OpenAPI schema for `@agentprism/client` to
 * generate a type from. Kept in sync with
 * `AgentPrism.Abstractions/Runs/RunEventType.cs` by hand.
 */
export type RunEventType =
  | 'RunStarted'
  | 'MessageDelta'
  | 'MessageCompleted'
  | 'ToolInvoking'
  | 'ToolInvoked'
  | 'ToolFailed'
  | 'RunCompleted'
  | 'RunFailed'
  | 'ChildRunStarted'
  | 'ChildRunCompleted'
  | 'HistoryCompacted'
  | 'WorkflowStarted'
  | 'SuperStepStarted'
  | 'SuperStepCompleted'
  | 'ExecutorInvoked'
  | 'ExecutorCompleted'
  | 'ExecutorFailed'
  | 'WorkflowOutput'
  | 'WorkflowRequest'
  | 'RunAwaitingInput'
  | 'ContentMasked'
  | 'ContentBlocked'
  | 'ModelFallbackUsed'
  | 'ReasoningDelta'
  | 'DocumentAttached'
  | 'RunContinuationBlocked'
  | 'ToolOutputTruncated'
  | 'StructuredResponseRejected'
  | 'StructuredResponseRepairAttempted'
  | 'Custom'
  | 'ChildRunTimedOut'
  | 'LoopIterationCompleted';

export interface RunEvent {
  runId: string;
  sequence: number;
  type: RunEventType;
  timestamp: string;
  text?: string | null;
  toolName?: string | null;
  toolCallId?: string | null;
  payload?: string | null;
  /** Names a 'Custom' event (phase 141). Null for every other event type. */
  customType?: string | null;
}
