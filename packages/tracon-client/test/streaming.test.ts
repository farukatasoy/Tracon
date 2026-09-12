import { describe, expect, it } from 'vitest';
import { createTraconClient, readSse } from '../src/index.js';

/**
 * The promise the guides make: a `text/event-stream` endpoint goes through the
 * SAME typed client, with `parseAs: 'stream'` plus `readSse`.
 *
 * `sse.test.ts` proves the decoder's framing rules on strings. It cannot prove
 * this, because the claim crosses a boundary the decoder never sees: whether
 * `openapi-fetch` hands the raw body back at all, and whether the middleware
 * this package installs lets a streaming response through untouched.
 */
function eventStreamResponse(chunks: string[]): Response {
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      const utf8 = new TextEncoder();

      for (const chunk of chunks) {
        controller.enqueue(utf8.encode(chunk));
      }

      controller.close();
    },
  });

  return new Response(stream, {
    status: 200,
    headers: { 'Content-Type': 'text/event-stream' },
  });
}

describe('streaming through the typed client', () => {
  it('reads a run stream as frames', async () => {
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async () =>
        eventStreamResponse([
          'id: 0\nevent: run\ndata: {"runId":"r1"}\n\n',
          ': keep-alive\n\n',
          'id: 1\nevent: done\ndata: {"ok":true}\n\n',
        ]),
    });

    const { response } = await client.POST('/api/agents/{name}/run', {
      params: { path: { name: 'support' } },
      body: { message: 'hello' },
      parseAs: 'stream',
    });

    const frames = [];

    for await (const frame of readSse(response)) {
      frames.push(frame);
    }

    // The keep-alive comment is not an event and must not reach the caller.
    expect(frames).toEqual([
      { id: '0', event: 'run', data: '{"runId":"r1"}' },
      { id: '1', event: 'done', data: '{"ok":true}' },
    ]);
  });

  it('reassembles a frame split across network chunks', async () => {
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async () => eventStreamResponse(['event: do', 'ne\ndata: par', 'tial\n\n']),
    });

    const { response } = await client.POST('/v1/responses', {
      body: { model: 'support', input: 'hello', stream: true },
      parseAs: 'stream',
    });

    const frames = [];

    for await (const frame of readSse(response)) {
      frames.push(frame);
    }

    expect(frames).toEqual([{ id: null, event: 'done', data: 'partial' }]);
  });

  it('still throws TraconError when a streaming call fails', async () => {
    // parseAs: 'stream' must not smuggle a failed response past the error
    // middleware — a 403 body is problem+json, not an event stream.
    const client = createTraconClient({
      baseUrl: 'https://example.test/tracon',
      fetch: async () =>
        new Response(JSON.stringify({ title: 'Forbidden', detail: 'Scope RunsWrite required' }), {
          status: 403,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    });

    await expect(
      client.POST('/api/agents/{name}/run', {
        params: { path: { name: 'support' } },
        body: { message: 'hello' },
        parseAs: 'stream',
      }),
    ).rejects.toMatchObject({ status: 403, title: 'Forbidden' });
  });
});
