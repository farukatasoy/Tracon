import type { ChatContent, ChatMessage, RunEvent } from './types';

/**
 * One rendered block of a conversation.
 *
 * Both live playground updates and recorded run events fold into this shape, so
 * a tool call looks the same whether it is streaming now or was replayed from
 * the database an hour later.
 */
export type TranscriptItem =
  | { kind: 'text'; id: string; text: string }
  | { kind: 'reasoning'; id: string; text: string }
  | { kind: 'error'; id: string; message: string }
  /** Context compaction ran. Not an error — a visible note that history was rewritten. */
  | { kind: 'compaction'; id: string; message: string; detail: string | null }
  | {
      /**
       * A tool call waiting for the operator's decision.
       *
       * Microsoft Agent Framework raises this instead of running a tool marked
       * `RequiresApproval`. The run ends here; the decision is the input of the
       * next turn.
       */
      kind: 'approval';
      id: string;
      requestId: string;
      name: string;
      args: string | null;
      decided: 'approved' | 'rejected' | null;
    }
  | {
      kind: 'tool';
      id: string;
      callId: string;
      name: string;
      args: string | null;
      result: string | null;
      error: string | null;
      state: 'running' | 'ok' | 'failed';
    };

/** Token counts observed while folding. */
export interface TranscriptUsage {
  inputTokens: number | null;
  outputTokens: number | null;
  totalTokens: number | null;
}

export interface TranscriptState {
  items: TranscriptItem[];
  usage: TranscriptUsage | null;
}

export const emptyTranscript: TranscriptState = { items: [], usage: null };

/**
 * Classifies a Microsoft.Extensions.AI content block.
 *
 * The `$type` discriminator is checked first, then the shape. MAF has renamed
 * discriminators between releases; falling back to the shape keeps a rename
 * from silently blanking the transcript.
 */
function classify(
  content: ChatContent,
): 'text' | 'reasoning' | 'call' | 'result' | 'usage' | 'approval' | 'other' {
  const type = typeof content.$type === 'string' ? content.$type.toLowerCase() : '';

  if (type === 'text') {
    return 'text';
  }

  // Shape check comes first for approvals: the discriminator name is the part
  // most likely to be renamed between MAF releases, while `requestId` plus a
  // nested `toolCall` is what the content actually is.
  if (type.includes('approvalrequest') || (typeof content['requestId'] === 'string' && content['toolCall'] !== undefined)) {
    return 'approval';
  }

  if (type === 'reasoning' || type === 'textreasoning') {
    return 'reasoning';
  }

  if (type === 'functioncall') {
    return 'call';
  }

  if (type === 'functionresult') {
    return 'result';
  }

  if (type === 'usage') {
    return 'usage';
  }

  if (typeof content.callId === 'string') {
    return typeof content.name === 'string' ? 'call' : 'result';
  }

  if (content.details !== undefined) {
    return 'usage';
  }

  return typeof content.text === 'string' ? 'text' : 'other';
}

function stringify(value: unknown): string | null {
  if (value === null || value === undefined) {
    return null;
  }

  if (typeof value === 'string') {
    return value;
  }

  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

/**
 * `boundary` stops a run of text from merging into an item that was written
 * before it — `foldMessages` sets it to the first item index OF the message
 * being folded, so this message's own text still extends within itself
 * (streaming deltas do) but never absorbs the previous message's last line.
 * `foldUpdate`/`foldRunEvents` fold one continuous stream and never pass it,
 * so their merging is unrestricted as before.
 */
function appendText(items: TranscriptItem[], kind: 'text' | 'reasoning', text: string, boundary = 0): void {
  const last = items[items.length - 1];

  if (last !== undefined && last.kind === kind && items.length - 1 >= boundary) {
    last.text += text;

    return;
  }

  items.push({ kind, id: `${kind}-${items.length}`, text });
}

function applyContent(state: TranscriptState, content: ChatContent, boundary = 0): void {
  switch (classify(content)) {
    case 'text': {
      if (typeof content.text === 'string' && content.text.length > 0) {
        appendText(state.items, 'text', content.text, boundary);
      }

      break;
    }

    case 'reasoning': {
      if (typeof content.text === 'string' && content.text.length > 0) {
        appendText(state.items, 'reasoning', content.text, boundary);
      }

      break;
    }

    case 'call': {
      const callId = typeof content.callId === 'string' ? content.callId : `call-${state.items.length}`;

      state.items.push({
        kind: 'tool',
        id: `tool-${callId}`,
        callId,
        name: typeof content.name === 'string' ? content.name : 'unknown',
        args: stringify(content.arguments ?? null),
        result: null,
        error: null,
        state: 'running',
      });

      break;
    }

    case 'result': {
      const callId = typeof content.callId === 'string' ? content.callId : null;
      const card = findTool(state.items, callId);

      if (card === null) {
        break;
      }

      const failure = stringify(content.exception ?? null);

      if (failure === null) {
        card.result = stringify(content.result ?? null);
        card.state = 'ok';
      } else {
        card.error = failure;
        card.state = 'failed';
      }

      break;
    }

    case 'approval': {
      const requestId = typeof content['requestId'] === 'string' ? content['requestId'] : null;

      if (requestId === null) {
        break;
      }

      const call = content['toolCall'] as ChatContent | undefined;

      state.items.push({
        kind: 'approval',
        id: `approval-${requestId}`,
        requestId,
        name: typeof call?.name === 'string' ? call.name : 'unknown',
        args: stringify(call?.arguments ?? null),
        decided: null,
      });

      break;
    }

    case 'usage': {
      const details = content.details;

      if (details !== undefined) {
        state.usage = {
          inputTokens: details.inputTokenCount ?? null,
          outputTokens: details.outputTokenCount ?? null,
          totalTokens: details.totalTokenCount ?? null,
        };
      }

      break;
    }

    default:
      break;
  }
}

function findTool(items: TranscriptItem[], callId: string | null): Extract<TranscriptItem, { kind: 'tool' }> | null {
  for (let index = items.length - 1; index >= 0; index--) {
    const item = items[index];

    if (item === undefined || item.kind !== 'tool') {
      continue;
    }

    if (callId === null || item.callId === callId) {
      return item;
    }
  }

  return null;
}

function clone(state: TranscriptState): TranscriptState {
  return {
    items: state.items.map((item) => ({ ...item })),
    usage: state.usage === null ? null : { ...state.usage },
  };
}

/**
 * Folds one streamed `AgentResponseUpdate` into the transcript.
 *
 * Returns a new state object; React needs a changed reference to re-render.
 */
export function foldUpdate(state: TranscriptState, update: { contents?: ChatContent[] }): TranscriptState {
  const next = clone(state);

  for (const content of update.contents ?? []) {
    applyContent(next, content);
  }

  return next;
}

/**
 * Folds a recorded chat history (`/api/sessions/{id}`) into one transcript per
 * message, in order.
 *
 * A tool call and its result are two SEPARATE `ChatMessage`s in MAF's history
 * (`assistant/functionCall` then `tool/functionResult`) — folding each message
 * on its own would create a fresh 'running' tool item for the call and have
 * nowhere to apply the result, leaving it stuck 'running' forever with the
 * actual result text visible nowhere (HATA-S4-016). Folding runs against one
 * shared accumulator instead, exactly as `foldRunEvents` already does across a
 * run's events, so a later message's result content can still find and update
 * the tool item an earlier message's call content created. Each message keeps
 * only the items it newly added — the result message itself typically adds
 * none, since it mutates the call's item in place by reference.
 */
export function foldMessages(messages: readonly ChatMessage[]): TranscriptState[] {
  const shared: TranscriptState = { items: [], usage: null };
  const perMessage: TranscriptState[] = [];

  for (const message of messages) {
    const before = shared.items.length;

    for (const content of message.contents ?? []) {
      applyContent(shared, content, before);
    }

    perMessage.push({ items: shared.items.slice(before), usage: shared.usage });
  }

  return perMessage;
}

/**
 * Folds recorded run events into a transcript.
 *
 * `MessageCompleted` carries the whole message. It is only used when no deltas
 * arrived — a non-streaming run produces the completion event alone, while a
 * streaming run would otherwise render its answer twice.
 */
export function foldRunEvents(events: readonly RunEvent[]): TranscriptState {
  const state: TranscriptState = { items: [], usage: null };
  let sawDelta = false;

  for (const event of events) {
    switch (event.type) {
      case 'MessageDelta': {
        if (typeof event.text === 'string' && event.text.length > 0) {
          appendText(state.items, 'text', event.text);
          sawDelta = true;
        }

        break;
      }

      case 'MessageCompleted': {
        if (!sawDelta && typeof event.text === 'string' && event.text.length > 0) {
          appendText(state.items, 'text', event.text);
        }

        break;
      }

      case 'ToolInvoking': {
        const callId = event.toolCallId ?? `seq-${event.sequence}`;

        state.items.push({
          kind: 'tool',
          id: `tool-${callId}-${event.sequence}`,
          callId,
          name: event.toolName ?? 'unknown',
          args: event.payload ?? null,
          result: null,
          error: null,
          state: 'running',
        });

        break;
      }

      case 'ToolInvoked': {
        const card = findTool(state.items, event.toolCallId ?? null);

        if (card !== null) {
          card.result = event.payload ?? null;
          card.state = 'ok';
        }

        break;
      }

      case 'ToolFailed': {
        const card = findTool(state.items, event.toolCallId ?? null);

        if (card !== null) {
          card.error = event.text ?? 'Tool failed.';
          card.state = 'failed';
        }

        break;
      }

      case 'RunFailed': {
        state.items.push({
          kind: 'error',
          id: `error-${event.sequence}`,
          message: event.text ?? 'Run failed.',
        });

        break;
      }

      case 'HistoryCompacted': {
        state.items.push({
          kind: 'compaction',
          id: `compaction-${event.sequence}`,
          message: event.text ?? 'History was compacted.',
          detail: event.payload ?? null,
        });

        break;
      }

      default:
        break;
    }
  }

  return state;
}
