/**
 * Server-Sent Events framing for Tracon's streaming endpoints.
 *
 * `openapi-fetch` can already hand back the raw stream (`parseAs: 'stream'`,
 * or the `response` every call returns), so streaming needs no special client
 * method — but every consumer would then have to write this decoder again.
 * It ships here instead: the Tracon UI is one consumer of it, not its
 * owner (Phase 159).
 */
/** Options for {@link readSse}. */
export interface ReadSseOptions {
  /**
   * Ends the stream with an {@link SseIdleTimeoutError} when no bytes at all
   * arrive for this long. Omitted means wait forever.
   *
   * 🚨 Measure BYTES, not frames. Tracon sends `: waiting` comments while a run
   * is still going and {@link SseDecoder} drops comments, so a healthy but
   * quiet run yields no frames for as long as the run is quiet — a
   * frame-based timeout would cut it off. Bytes tell the two apart: a run that
   * is still going keeps sending them, a finished one closes the stream, and a
   * connection that died without either sends nothing.
   *
   * Set it above the server's `RunEventPollInterval` (250 ms by default), with
   * room for a slow link: that interval is how often the keep-alive is sent,
   * and a threshold under it would fire between two healthy keep-alives.
   */
  idleTimeoutMs?: number;
}

/**
 * Thrown by {@link readSse} when a stream goes completely quiet for longer
 * than {@link ReadSseOptions.idleTimeoutMs}.
 *
 * A connection that drops without a close frame — a laptop going to sleep, a
 * proxy timing out, a network that goes away mid-body — leaves the reader
 * waiting on a chunk that will never come and throws nothing at all. Without
 * this, a consumer cannot tell that from a run that is simply still working:
 * both look like silence forever.
 */
export class SseIdleTimeoutError extends Error {
  constructor(idleTimeoutMs: number) {
    super(`The event stream sent nothing for ${idleTimeoutMs}ms and was closed.`);

    this.name = 'SseIdleTimeoutError';
    this.idleTimeoutMs = idleTimeoutMs;
  }

  /** The threshold that was exceeded, in milliseconds. */
  readonly idleTimeoutMs: number;
}

/** One Server-Sent Events frame. */
export interface SseFrame {
  /** Value of the `id:` field, when present. */
  id: string | null;
  /** Value of the `event:` field. Defaults to `message`, as the spec requires. */
  event: string;
  /** Joined `data:` lines. */
  data: string;
}

/**
 * Incremental SSE decoder.
 *
 * Kept as a pure class with no I/O so the framing rules can be unit tested:
 * chunk boundaries fall in arbitrary places and a parser that assumes whole
 * frames per chunk works right up until a slow network proves it wrong.
 *
 * Comment lines (`: waiting`) are skipped. Tracon sends them as keep-alives
 * while a run is still in progress.
 */
export class SseDecoder {
  #buffer = '';
  #data: string[] = [];
  #event: string | null = null;
  #id: string | null = null;

  /** Feeds a chunk of text and returns every frame it completed. */
  push(chunk: string): SseFrame[] {
    this.#buffer += chunk;

    const frames: SseFrame[] = [];
    let newline = this.#nextLineBreak();

    while (newline !== null) {
      const line = this.#buffer.slice(0, newline.index);

      this.#buffer = this.#buffer.slice(newline.index + newline.length);

      const frame = this.#consume(line);

      if (frame !== null) {
        frames.push(frame);
      }

      newline = this.#nextLineBreak();
    }

    return frames;
  }

  #nextLineBreak(): { index: number; length: number } | null {
    for (let index = 0; index < this.#buffer.length; index++) {
      const char = this.#buffer[index];

      if (char === '\n') {
        return { index, length: 1 };
      }

      if (char === '\r') {
        // A trailing lone "\r" may be the first half of "\r\n"; wait for more.
        if (index === this.#buffer.length - 1) {
          return null;
        }

        return this.#buffer[index + 1] === '\n'
          ? { index, length: 2 }
          : { index, length: 1 };
      }
    }

    return null;
  }

  #consume(line: string): SseFrame | null {
    if (line.length === 0) {
      return this.#flush();
    }

    if (line.startsWith(':')) {
      return null;
    }

    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? '' : line.slice(colon + 1);

    if (value.startsWith(' ')) {
      value = value.slice(1);
    }

    switch (field) {
      case 'data':
        this.#data.push(value);
        break;
      case 'event':
        this.#event = value;
        break;
      case 'id':
        this.#id = value;
        break;
      default:
        break;
    }

    return null;
  }

  #flush(): SseFrame | null {
    if (this.#data.length === 0 && this.#event === null) {
      return null;
    }

    const frame: SseFrame = {
      id: this.#id,
      event: this.#event ?? 'message',
      data: this.#data.join('\n'),
    };

    this.#data = [];
    this.#event = null;
    this.#id = null;

    return frame;
  }
}

/**
 * Reads a `text/event-stream` response body as frames.
 *
 * `EventSource` is not used: it cannot send an `Authorization` header and it
 * cannot issue a POST, and Tracon needs both.
 */
export async function* readSse(
  response: Response,
  options?: ReadSseOptions,
): AsyncGenerator<SseFrame> {
  if (response.body === null) {
    return;
  }

  const reader = response.body.getReader();
  const utf8 = new TextDecoder();
  const decoder = new SseDecoder();
  const idleTimeoutMs = options?.idleTimeoutMs;

  try {
    for (;;) {
      const { done, value } =
        idleTimeoutMs === undefined ? await reader.read() : await readWithin(reader, idleTimeoutMs);

      if (done) {
        break;
      }

      for (const frame of decoder.push(utf8.decode(value, { stream: true }))) {
        yield frame;
      }
    }
  } finally {
    reader.releaseLock();
  }
}

/**
 * Reads the next chunk, or throws {@link SseIdleTimeoutError} when nothing at
 * all arrives within `idleTimeoutMs`.
 *
 * The reader is cancelled on the way out so the underlying connection is
 * released rather than left half-open behind a generator nobody is pulling.
 */
async function readWithin(
  reader: ReadableStreamDefaultReader<Uint8Array>,
  idleTimeoutMs: number,
): Promise<{ done: boolean; value?: Uint8Array }> {
  let timer: ReturnType<typeof setTimeout> | undefined;

  const idle = new Promise<never>((_resolve, reject) => {
    timer = setTimeout(() => reject(new SseIdleTimeoutError(idleTimeoutMs)), idleTimeoutMs);
  });

  try {
    return await Promise.race([reader.read(), idle]);
  } catch (error) {
    if (error instanceof SseIdleTimeoutError) {
      await reader.cancel().catch(() => undefined);
    }

    throw error;
  } finally {
    clearTimeout(timer);
  }
}
