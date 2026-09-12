/**
 * Server-Sent Events framing for Tracon's streaming endpoints.
 *
 * `openapi-fetch` can already hand back the raw stream (`parseAs: 'stream'`,
 * or the `response` every call returns), so streaming needs no special client
 * method — but every consumer would then have to write this decoder again.
 * It ships here instead: the Tracon UI is one consumer of it, not its
 * owner (Phase 159).
 */
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
export async function* readSse(response: Response): AsyncGenerator<SseFrame> {
  if (response.body === null) {
    return;
  }

  const reader = response.body.getReader();
  const utf8 = new TextDecoder();
  const decoder = new SseDecoder();

  try {
    for (;;) {
      const { done, value } = await reader.read();

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
