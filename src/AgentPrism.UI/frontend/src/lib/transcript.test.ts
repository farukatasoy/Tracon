import { describe, expect, it } from 'vitest';
import { applyApprovalPresentations, emptyTranscript, foldMessages, foldRunEvents, foldUpdate } from './transcript';
import type { ChatMessage } from '@agentprism/client';
import type { RunEvent } from './run-event';

/**
 * `foldMessages` deliberately reads `ChatMessage.contents` through a
 * shape-sniffing fallback, not the generated `AiContent` discriminated union
 * (see the `ChatContent` comment in transcript.ts) — these fixtures build the
 * loose wire shape that fallback is FOR, then cross the boundary with the
 * same cast `foldMessages` itself uses internally.
 */
interface LooseChatMessage {
  role: string;
  contents: Array<Record<string, unknown>>;
}

const event = (partial: Partial<RunEvent> & Pick<RunEvent, 'type' | 'sequence'>): RunEvent => ({
  runId: 'run-1',
  timestamp: '2026-08-02T10:00:00Z',
  ...partial,
});

describe('foldUpdate', () => {
  it('appends streamed text into one block', () => {
    let state = emptyTranscript;

    state = foldUpdate(state, { contents: [{ $type: 'text', text: 'Hel' }] });
    state = foldUpdate(state, { contents: [{ $type: 'text', text: 'lo' }] });

    expect(state.items).toEqual([{ kind: 'text', id: 'text-0', text: 'Hello' }]);
  });

  it('returns a new state object so React re-renders', () => {
    const before = emptyTranscript;
    const after = foldUpdate(before, { contents: [{ $type: 'text', text: 'x' }] });

    expect(after).not.toBe(before);
    expect(before.items).toHaveLength(0);
  });

  it('opens a tool card on a call and completes it on the result', () => {
    let state = emptyTranscript;

    state = foldUpdate(state, {
      contents: [{ $type: 'functionCall', callId: 'c1', name: 'get_order_status', arguments: { orderId: 'ORD-7' } }],
    });

    expect(state.items[0]).toMatchObject({ kind: 'tool', name: 'get_order_status', state: 'running' });

    state = foldUpdate(state, {
      contents: [{ $type: 'functionResult', callId: 'c1', result: 'shipped' }],
    });

    expect(state.items[0]).toMatchObject({ kind: 'tool', state: 'ok', result: 'shipped' });
  });

  it('marks a tool card as failed when the result carries an exception', () => {
    let state = emptyTranscript;

    state = foldUpdate(state, { contents: [{ $type: 'functionCall', callId: 'c1', name: 't' }] });
    state = foldUpdate(state, { contents: [{ $type: 'functionResult', callId: 'c1', exception: 'boom' }] });

    expect(state.items[0]).toMatchObject({ kind: 'tool', state: 'failed', error: 'boom' });
  });

  it('classifies by shape when the discriminator is unknown', () => {
    // MAF has renamed discriminators between releases. Falling back to the
    // shape keeps a rename from silently blanking the transcript.
    let state = emptyTranscript;

    state = foldUpdate(state, { contents: [{ $type: 'somethingNew', callId: 'c9', name: 'tool_x' }] });

    expect(state.items[0]).toMatchObject({ kind: 'tool', name: 'tool_x', callId: 'c9' });
  });

  it('collects usage details', () => {
    const state = foldUpdate(emptyTranscript, {
      contents: [{ $type: 'usage', details: { inputTokenCount: 12, outputTokenCount: 5, totalTokenCount: 17 } }],
    });

    expect(state.usage).toEqual({ inputTokens: 12, outputTokens: 5, totalTokens: 17 });
  });

  it('keeps text blocks separate across a tool call', () => {
    let state = emptyTranscript;

    state = foldUpdate(state, { contents: [{ $type: 'text', text: 'before' }] });
    state = foldUpdate(state, { contents: [{ $type: 'functionCall', callId: 'c1', name: 't' }] });
    state = foldUpdate(state, { contents: [{ $type: 'text', text: 'after' }] });

    expect(state.items.map((item) => item.kind)).toEqual(['text', 'tool', 'text']);
  });
});

describe('foldRunEvents', () => {
  it('joins deltas and ignores the duplicate completion', () => {
    const state = foldRunEvents([
      event({ type: 'RunStarted', sequence: 0 }),
      event({ type: 'MessageDelta', sequence: 1, text: 'Ank' }),
      event({ type: 'MessageDelta', sequence: 2, text: 'ara' }),
      event({ type: 'MessageCompleted', sequence: 3, text: 'Ankara' }),
      event({ type: 'RunCompleted', sequence: 4 }),
    ]);

    expect(state.items).toEqual([{ kind: 'text', id: 'text-0', text: 'Ankara' }]);
  });

  it('uses the completion when no deltas arrived', () => {
    // A non-streaming run produces the completion event alone.
    const state = foldRunEvents([
      event({ type: 'RunStarted', sequence: 0 }),
      event({ type: 'MessageCompleted', sequence: 1, text: 'whole answer' }),
    ]);

    expect(state.items).toEqual([{ kind: 'text', id: 'text-0', text: 'whole answer' }]);
  });

  it('pairs tool events by call id', () => {
    const state = foldRunEvents([
      event({ type: 'ToolInvoking', sequence: 1, toolCallId: 'a', toolName: 'first', payload: 'x=1' }),
      event({ type: 'ToolInvoking', sequence: 2, toolCallId: 'b', toolName: 'second', payload: 'y=2' }),
      event({ type: 'ToolInvoked', sequence: 3, toolCallId: 'a', payload: 'done a' }),
    ]);

    expect(state.items[0]).toMatchObject({ name: 'first', state: 'ok', result: 'done a' });
    expect(state.items[1]).toMatchObject({ name: 'second', state: 'running' });
  });

  it('records a failed tool call', () => {
    const state = foldRunEvents([
      event({ type: 'ToolInvoking', sequence: 1, toolCallId: 'a', toolName: 'first' }),
      event({ type: 'ToolFailed', sequence: 2, toolCallId: 'a', text: 'timed out' }),
    ]);

    expect(state.items[0]).toMatchObject({ state: 'failed', error: 'timed out' });
  });

  it('surfaces a failed run as an error block', () => {
    const state = foldRunEvents([
      event({ type: 'RunStarted', sequence: 0 }),
      event({ type: 'RunFailed', sequence: 1, text: 'model unavailable' }),
    ]);

    expect(state.items).toEqual([{ kind: 'error', id: 'error-1', message: 'model unavailable' }]);
  });
});

describe('foldMessages', () => {
  it('folds a plain user/assistant exchange one item per message', () => {
    const messages: LooseChatMessage[] = [
      { role: 'user', contents: [{ $type: 'text', text: 'Hello' }] },
      { role: 'assistant', contents: [{ $type: 'text', text: 'Hi there' }] },
    ];

    const folds = foldMessages(messages as unknown as ChatMessage[]);

    expect(folds).toHaveLength(2);
    expect(folds[0]?.items).toEqual([{ kind: 'text', id: 'text-0', text: 'Hello' }]);
    // A fresh item, not merged into the previous message's — `id` counts from
    // the shared accumulator, so the second message's own item is "text-1".
    expect(folds[1]?.items).toEqual([{ kind: 'text', id: 'text-1', text: 'Hi there' }]);
  });

  it('completes a tool call whose result arrives in the NEXT message', () => {
    // MAF splits a call and its result across two separate ChatMessages
    // (assistant/functionCall, then tool/functionResult) — folding each
    // message on its own (the pre-fix `foldMessage`) left the call stuck
    // 'running' forever, since the result had nowhere to apply (HATA-S4-016).
    const messages: LooseChatMessage[] = [
      { role: 'user', contents: [{ $type: 'text', text: 'Where is my order?' }] },
      {
        role: 'assistant',
        contents: [{ $type: 'functionCall', callId: 'c1', name: 'get_order_status', arguments: { orderId: 'ORD-1001' } }],
      },
      { role: 'tool', contents: [{ $type: 'functionResult', callId: 'c1', result: 'shipped' }] },
      { role: 'assistant', contents: [{ $type: 'text', text: 'Your order shipped.' }] },
    ];

    const folds = foldMessages(messages as unknown as ChatMessage[]);

    expect(folds).toHaveLength(4);
    // The call's own message now shows the completed card, result included.
    expect(folds[1]?.items).toEqual([
      {
        kind: 'tool',
        id: 'tool-c1',
        callId: 'c1',
        name: 'get_order_status',
        args: JSON.stringify({ orderId: 'ORD-1001' }, null, 2),
        result: 'shipped',
        error: null,
        state: 'ok',
      },
    ]);
    // The result message mutated that same item in place; it adds nothing of
    // its own (the screen falls back to "no content" for this row, same as
    // it always has — the result text is no longer invisible, just attached
    // to the call instead).
    expect(folds[2]?.items).toEqual([]);
  });

  it('leaves an unmatched result inert rather than crashing', () => {
    const messages: LooseChatMessage[] = [
      { role: 'tool', contents: [{ $type: 'functionResult', callId: 'no-such-call', result: 'x' }] },
    ];

    expect(foldMessages(messages as unknown as ChatMessage[])[0]?.items).toEqual([]);
  });

  it('returns one state per message even for an empty message list', () => {
    expect(foldMessages([])).toEqual([]);
  });
});

describe('approval presentation', () => {
  it('starts every approval card with no presentation, filled in only once the frame arrives', () => {
    const state = foldUpdate(emptyTranscript, {
      contents: [{ requestId: 'req-1', toolCall: { name: 'delete_skill', arguments: { skillId: 'x' } } }],
    });

    expect(state.items).toEqual([
      {
        kind: 'approval',
        id: 'approval-req-1',
        requestId: 'req-1',
        name: 'delete_skill',
        args: JSON.stringify({ skillId: 'x' }, null, 2),
        decided: null,
        entityType: null,
        entityId: null,
        entityName: null,
        message: null,
      },
    ]);
  });

  it('fills in the matching approval card by requestId', () => {
    const before = foldUpdate(emptyTranscript, {
      contents: [{ requestId: 'req-1', toolCall: { name: 'delete_skill' } }],
    });

    const after = applyApprovalPresentations(before, [
      {
        requestId: 'req-1',
        toolName: 'delete_skill',
        entityType: 'skill',
        entityId: '8f14e45f-ceea-467e-adc9-15476f4f0f47',
        entityName: 'Refund Policy',
        message: null,
      },
    ]);

    expect(after.items[0]).toMatchObject({
      entityType: 'skill',
      entityId: '8f14e45f-ceea-467e-adc9-15476f4f0f47',
      entityName: 'Refund Policy',
    });
  });

  it('leaves every other item, and an unmatched request, untouched', () => {
    const before = foldUpdate(emptyTranscript, {
      contents: [
        { $type: 'text', text: 'hello' },
        { requestId: 'req-1', toolCall: { name: 'delete_skill' } },
      ],
    });

    const after = applyApprovalPresentations(before, [
      { requestId: 'req-does-not-exist', toolName: 'other_tool', entityType: null, entityId: null, entityName: 'Should not apply', message: null },
    ]);

    expect(after).toEqual(before);
  });

  it('is a no-op for an empty announcement list, returning the SAME state reference', () => {
    const before = foldUpdate(emptyTranscript, { contents: [{ requestId: 'req-1', toolCall: { name: 'delete_skill' } }] });

    expect(applyApprovalPresentations(before, [])).toBe(before);
  });
});
