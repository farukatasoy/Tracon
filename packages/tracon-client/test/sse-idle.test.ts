import { describe, expect, it } from 'vitest';
import { readSse, SseIdleTimeoutError } from '../src/index.js';

/**
 * A connection that dies without a close frame — a laptop sleeping, a proxy
 * timing out, a network that goes away mid-body — leaves the reader waiting on
 * a chunk that never comes and throws nothing at all. The run detail screen
 * sat on "Waiting for events…" forever because of it (HATA-S3-005), with no
 * way for the reader to tell that apart from a run that is simply still
 * working.
 */
function streamThat(
  write: (controller: ReadableStreamDefaultController<Uint8Array>) => void,
  onCancel?: () => void,
): Response {
  const stream = new ReadableStream<Uint8Array>({
    start: write,
    cancel: onCancel,
  });

  return new Response(stream, {
    status: 200,
    headers: { 'Content-Type': 'text/event-stream' },
  });
}

describe('readSse idle timeout', () => {
  it('ends a stream that goes completely quiet', async () => {
    // Enqueues one frame and then never writes or closes again.
    const response = streamThat((controller) => {
      controller.enqueue(new TextEncoder().encode('event: run\ndata: {"runId":"r1"}\n\n'));
    });

    const frames = [];
    let thrown: unknown;

    try {
      for await (const frame of readSse(response, { idleTimeoutMs: 40 })) {
        frames.push(frame);
      }
    } catch (error) {
      thrown = error;
    }

    expect(frames).toHaveLength(1);
    expect(thrown).toBeInstanceOf(SseIdleTimeoutError);
    expect((thrown as SseIdleTimeoutError).idleTimeoutMs).toBe(40);
  });

  it('releases the connection when it gives up', async () => {
    // Left half-open, the dead socket stays allocated behind a generator
    // nobody is pulling from any more.
    let cancelled = false;

    const response = streamThat(
      (controller) => {
        controller.enqueue(new TextEncoder().encode(': waiting\n\n'));
      },
      () => {
        cancelled = true;
      },
    );

    await expect(async () => {
      for await (const _ of readSse(response, { idleTimeoutMs: 30 })) {
        // drain
      }
    }).rejects.toBeInstanceOf(SseIdleTimeoutError);

    expect(cancelled).toBe(true);
  });

  it('counts BYTES, so keep-alive comments hold a quiet run open', async () => {
    // 🚨 SseDecoder drops comment lines, so a healthy run that is thinking
    // yields NO frames while it thinks — only `: waiting` bytes. A timeout
    // measured in frames would cut that run off; this one must not.
    const response = streamThat((controller) => {
      const utf8 = new TextEncoder();
      let sent = 0;

      const tick = setInterval(() => {
        sent += 1;

        if (sent <= 6) {
          controller.enqueue(utf8.encode(': waiting\n\n'));
          return;
        }

        clearInterval(tick);
        controller.enqueue(utf8.encode('event: done\ndata: {"ok":true}\n\n'));
        controller.close();
      }, 10);
    });

    const frames = [];

    for await (const frame of readSse(response, { idleTimeoutMs: 50 })) {
      frames.push(frame);
    }

    // 60ms of keep-alives under a 50ms threshold: no single gap exceeded it.
    expect(frames).toEqual([{ id: null, event: 'done', data: '{"ok":true}' }]);
  });

  it('waits forever when no threshold is given', async () => {
    const response = streamThat((controller) => {
      const utf8 = new TextEncoder();

      setTimeout(() => {
        controller.enqueue(utf8.encode('event: done\ndata: {"ok":true}\n\n'));
        controller.close();
      }, 60);
    });

    const frames = [];

    for await (const frame of readSse(response)) {
      frames.push(frame);
    }

    expect(frames).toHaveLength(1);
  });
});
