import { describe, expect, it } from 'vitest';
import { SseDecoder } from './sse';

describe('SseDecoder', () => {
  it('decodes a single frame', () => {
    const frames = new SseDecoder().push('id: 3\nevent: run.started\ndata: {"a":1}\n\n');

    expect(frames).toEqual([{ id: '3', event: 'run.started', data: '{"a":1}' }]);
  });

  it('joins multi-line data with newlines', () => {
    const frames = new SseDecoder().push('event: update\ndata: line one\ndata: line two\n\n');

    expect(frames[0]?.data).toBe('line one\nline two');
  });

  it('survives frames split across chunks', () => {
    const decoder = new SseDecoder();

    // The network decides where a chunk ends. A parser that assumes whole
    // frames per chunk works until it does not.
    expect(decoder.push('event: upda')).toEqual([]);
    expect(decoder.push('te\ndata: hel')).toEqual([]);
    expect(decoder.push('lo\n')).toEqual([]);

    const frames = decoder.push('\n');

    expect(frames).toEqual([{ id: null, event: 'update', data: 'hello' }]);
  });

  it('emits several frames from one chunk', () => {
    const frames = new SseDecoder().push('event: a\ndata: 1\n\nevent: b\ndata: 2\n\n');

    expect(frames.map((frame) => frame.event)).toEqual(['a', 'b']);
  });

  it('ignores comment keep-alives', () => {
    const decoder = new SseDecoder();

    // AgentPrism sends `: waiting` while a run is still in progress.
    expect(decoder.push(': waiting\n\n')).toEqual([]);
    expect(decoder.push('event: done\ndata: {}\n\n')).toHaveLength(1);
  });

  it('handles CRLF line endings', () => {
    const frames = new SseDecoder().push('event: update\r\ndata: hi\r\n\r\n');

    expect(frames).toEqual([{ id: null, event: 'update', data: 'hi' }]);
  });

  it('waits for the second half of a split CRLF', () => {
    const decoder = new SseDecoder();

    expect(decoder.push('data: hi\r')).toEqual([]);
    expect(decoder.push('\n\r\n')).toEqual([{ id: null, event: 'message', data: 'hi' }]);
  });

  it('defaults the event name to message', () => {
    const frames = new SseDecoder().push('data: plain\n\n');

    expect(frames[0]?.event).toBe('message');
  });

  it('strips exactly one leading space from a value', () => {
    const frames = new SseDecoder().push('data:  two spaces\n\n');

    expect(frames[0]?.data).toBe(' two spaces');
  });
});
