import { describe, expect, it } from 'vitest';
import { extractText, findFunctionCalls, type RunResult } from './client.ts';

function result(messages: RunResult['response']['messages']): RunResult {
  return { runId: 'r1', sessionId: 's1', response: { messages } };
}

describe('findFunctionCalls', () => {
  it('collects every functionCall content across messages', () => {
    const calls = findFunctionCalls(
      result([
        { role: 'assistant', contents: [{ $type: 'functionCall', callId: 'c1', name: 'read_page_title' }] },
      ]),
    );

    expect(calls).toHaveLength(1);
    expect(calls[0]?.callId).toBe('c1');
    expect(calls[0]?.name).toBe('read_page_title');
  });

  it('returns an empty array when there is no functionCall content', () => {
    const calls = findFunctionCalls(result([{ role: 'assistant', contents: [{ $type: 'text', text: 'hi' }] }]));

    expect(calls).toHaveLength(0);
  });
});

describe('extractText', () => {
  it('joins every text content across messages, in order', () => {
    const text = extractText(
      result([
        { role: 'assistant', contents: [{ $type: 'text', text: 'Title:' }] },
        { role: 'assistant', contents: [{ $type: 'text', text: 'Shopping cart' }] },
      ]),
    );

    expect(text).toBe('Title:\nShopping cart');
  });

  it('ignores non-text content', () => {
    const text = extractText(
      result([{ role: 'assistant', contents: [{ $type: 'functionCall', callId: 'c1', name: 't' }] }]),
    );

    expect(text).toBe('');
  });
});
