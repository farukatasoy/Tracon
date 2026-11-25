/**
 * A client for Tracon's voice conversation WebSocket (`UseVoiceConversation()`).
 *
 * The protocol is written down at https://tracon.dev/reference/voice-conversation-protocol/ and its frames
 * are described by https://tracon.dev/schemas/voice-conversation-frames.schema.json. This module is the
 * typed reading of both: the frames as a discriminated union, the handshake (the token travels in a
 * subprotocol, never in the address), and the rules a client otherwise learns by hanging.
 *
 * It does not touch a microphone or a speaker and it does not detect the end of speech: the server runs no
 * voice activity detection, so the caller decides when to {@link VoiceConversation.commit}.
 */

/** The WebSocket subprotocol the server echoes back on a successful handshake. */
export const VOICE_SUBPROTOCOL = 'tracon.voice.v1';

/** The prefix of the subprotocol that carries the bearer token. */
export const VOICE_TOKEN_PREFIX = 'tracon.token.';

/** The audio a client sends: `webm-opus` is what `MediaRecorder` produces, `pcm16` is raw mono 16-bit little-endian PCM. */
export type VoiceInputFormat = 'webm-opus' | 'pcm16';

/** A text frame the client sends. Audio goes as binary frames with {@link VoiceConversation.sendAudio}. */
export type VoiceClientFrame =
  | { type: 'start'; agent: string; voiceId?: string; inputFormat?: VoiceInputFormat }
  | { type: 'commit' }
  | { type: 'cancel' }
  | { type: 'stop' };

/** The `ready` frame: the server accepted `start` and the client may send audio. */
export interface VoiceReadyFrame {
  type: 'ready';
  agent: string;
  sessionId: string;
  /** Whether the audio of the answers is stored. Show it to the user: nothing is recorded silently. */
  persistAudio: boolean;
}

/** A text frame the server sends. Binary frames are audio and arrive as {@link VoiceEvent} `audio` events. */
export type VoiceServerFrame =
  | VoiceReadyFrame
  | { type: 'transcript'; text: string; final: boolean }
  | { type: 'runStarted'; runId: string }
  | { type: 'text'; delta: string }
  | { type: 'audioStart'; mediaType: string }
  | { type: 'audioEnd' }
  | { type: 'error'; message: string }
  | { type: 'done'; cancelled: boolean; turn: number; attachmentId?: string }
  | { type: 'idle' }
  | { type: 'ended'; reason: 'agent' };

/** What {@link VoiceConversation.events} yields, in the order it happened. */
export type VoiceEvent =
  /** A text frame of the protocol. */
  | { kind: 'frame'; frame: VoiceServerFrame }
  /** A chunk of the answer's audio, between an `audioStart` and an `audioEnd`. */
  | { kind: 'audio'; data: Uint8Array }
  /**
   * A text frame this client does not know: a newer server, or not JSON. It is safe to ignore; the protocol adds
   * frames without a new subprotocol.
   */
  | { kind: 'unknown'; text: string }
  /** The socket closed. It is always the last event. */
  | { kind: 'closed'; code: number; reason: string; clean: boolean };

/** The part of a `WebSocket` this client uses; the browser's `WebSocket` and Node's both satisfy it. */
export interface WebSocketLike {
  binaryType: string;
  readonly readyState: number;
  send(data: string | ArrayBufferLike | ArrayBufferView): void;
  close(code?: number, reason?: string): void;
  onopen: ((event: unknown) => void) | null;
  onmessage: ((event: { data: unknown }) => void) | null;
  onerror: ((event: unknown) => void) | null;
  onclose: ((event: { code: number; reason: string; wasClean: boolean }) => void) | null;
}

/** Opens a socket. The default uses the global `WebSocket`. */
export type WebSocketFactory = (url: string, protocols: string[]) => WebSocketLike;

/** Options for {@link createVoiceConversation}. */
export interface VoiceConversationOptions {
  /**
   * The application root plus the `MapTracon` prefix, like `createTraconClient`'s `baseUrl`:
   * `https://example.com/tracon`. `http` becomes `ws` and `https` becomes `wss`.
   */
  baseUrl: string;

  /** The agent session the conversation runs in. A session the server has never seen is opened by the first turn. */
  sessionId: string;

  /** The agent to talk to. */
  agent: string;

  /** The bearer token or API key (an API key needs the `RunsWrite` scope), or a function that returns one. */
  token?: string | (() => string | null | undefined);

  /**
   * The voice the answers are spoken in. A host that decides the voice on the server ignores it, so a front end
   * for a multi-tenant host usually leaves it out.
   */
  voiceId?: string;

  /** The audio this client sends. The server's default is `webm-opus`. */
  inputFormat?: VoiceInputFormat;

  /**
   * Opens the socket. Browsers and Node 22 and later have a global `WebSocket`, which is the default; pass a
   * factory to use another implementation, or a test double.
   */
  webSocket?: WebSocketFactory;
}

/**
 * Thrown when a call is made in a state the protocol does not allow: audio before `ready`, or any send after the
 * conversation ended or the socket closed.
 */
export class VoiceConversationStateError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'VoiceConversationStateError';
  }
}

/** One voice conversation. */
export interface VoiceConversation {
  /** The address the socket was opened on. */
  readonly url: string;

  /**
   * Resolves with the `ready` frame. Rejects when the conversation fails first: an `error` frame before `ready`, or
   * the socket closing.
   */
  readonly ready: Promise<VoiceReadyFrame>;

  /**
   * Every event of the conversation, in order, ending with the `closed` event. It can be taken once. Events that
   * arrive before it is taken are kept.
   */
  events(): AsyncIterable<VoiceEvent>;

  /** Sends a chunk of the utterance being spoken. Allowed from `ready` until the conversation ends. */
  sendAudio(chunk: ArrayBufferLike | ArrayBufferView): void;

  /** The utterance ended: answer now. Every commit the server accepts ends in a `done` or an `idle` frame. */
  commit(): void;

  /** Barge-in: stop the answer that is being generated or spoken. */
  cancel(): void;

  /** Asks the server to close the conversation cleanly. The `closed` event follows. */
  stop(): void;

  /** Closes the socket now, without telling the server why. Prefer {@link VoiceConversation.stop}. */
  close(): void;
}

const KNOWN_FRAMES: ReadonlySet<string> = new Set([
  'ready',
  'transcript',
  'runStarted',
  'text',
  'audioStart',
  'audioEnd',
  'error',
  'done',
  'idle',
  'ended',
]);

const OPEN = 1;

/**
 * The WebSocket address of a conversation.
 *
 * `baseUrl` is the application root plus the `MapTracon` prefix. The scheme follows it: `https` is `wss`.
 */
export function voiceStreamUrl(baseUrl: string, sessionId: string): string {
  const url = new URL(`${baseUrl.replace(/\/+$/, '')}/api/voice/sessions/${encodeURIComponent(sessionId)}/stream`);

  url.protocol = url.protocol === 'https:' || url.protocol === 'wss:' ? 'wss:' : 'ws:';

  return url.toString();
}

/**
 * The subprotocols to offer in the handshake. The token travels here and not in the address: an address is
 * written to server logs, proxy logs and browser history, and a browser cannot add an `Authorization` header to a
 * WebSocket handshake.
 */
export function voiceSubProtocols(token?: string | null): string[] {
  return token === undefined || token === null || token.length === 0
    ? [VOICE_SUBPROTOCOL]
    : [VOICE_SUBPROTOCOL, VOICE_TOKEN_PREFIX + token];
}

/**
 * Reads a text frame. Returns `null` for text that is not a JSON object with a known `type`; the caller ignores
 * what it does not know.
 */
export function parseVoiceFrame(text: string): VoiceServerFrame | null {
  let parsed: unknown;

  try {
    parsed = JSON.parse(text);
  } catch {
    return null;
  }

  if (typeof parsed !== 'object' || parsed === null) {
    return null;
  }

  const type = (parsed as { type?: unknown }).type;

  return typeof type === 'string' && KNOWN_FRAMES.has(type) ? (parsed as VoiceServerFrame) : null;
}

/**
 * Opens a voice conversation: the socket, the handshake and the `start` frame.
 *
 * ```ts
 * const conversation = createVoiceConversation({
 *   baseUrl: 'https://example.com/tracon',
 *   sessionId: 'lesson-42',
 *   agent: 'coach',
 *   token,
 * });
 *
 * await conversation.ready;
 * conversation.sendAudio(chunk);
 * conversation.commit();
 *
 * for await (const event of conversation.events()) {
 *   if (event.kind === 'frame' && event.frame.type === 'done') break;
 * }
 * ```
 */
export function createVoiceConversation(options: VoiceConversationOptions): VoiceConversation {
  const url = voiceStreamUrl(options.baseUrl, options.sessionId);
  const token = typeof options.token === 'function' ? options.token() : options.token;
  const open = options.webSocket ?? defaultWebSocket;
  const socket = open(url, voiceSubProtocols(token));

  socket.binaryType = 'arraybuffer';

  const queue: VoiceEvent[] = [];
  let waiting: ((result: IteratorResult<VoiceEvent>) => void) | null = null;
  let taken = false;
  let finished = false;
  let state: 'connecting' | 'ready' | 'ended' | 'closed' = 'connecting';

  let resolveReady!: (frame: VoiceReadyFrame) => void;
  let rejectReady!: (reason: Error) => void;

  const ready = new Promise<VoiceReadyFrame>((resolve, reject) => {
    resolveReady = resolve;
    rejectReady = reject;
  });

  // A conversation whose caller never awaits `ready` must not raise an unhandled rejection when it fails.
  ready.catch(() => undefined);

  const push = (event: VoiceEvent): void => {
    if (waiting !== null) {
      const resolve = waiting;

      waiting = null;
      resolve({ value: event, done: false });
    } else {
      queue.push(event);
    }
  };

  const finish = (): void => {
    finished = true;

    if (waiting !== null) {
      const resolve = waiting;

      waiting = null;
      resolve({ value: undefined, done: true });
    }
  };

  const send = (frame: VoiceClientFrame): void => {
    socket.send(JSON.stringify(frame));
  };

  const requireReady = (what: string): void => {
    if (state !== 'ready') {
      throw new VoiceConversationStateError(
        state === 'connecting'
          ? `${what} is sent after the server's 'ready' frame; await conversation.ready first.`
          : `${what} cannot be sent: the conversation has ${state === 'ended' ? 'ended' : 'closed'}.`,
      );
    }
  };

  socket.onopen = () => {
    send({
      type: 'start',
      agent: options.agent,
      ...(options.voiceId === undefined ? {} : { voiceId: options.voiceId }),
      ...(options.inputFormat === undefined ? {} : { inputFormat: options.inputFormat }),
    });
  };

  socket.onmessage = (event) => {
    const data = event.data;

    if (typeof data === 'string') {
      const frame = parseVoiceFrame(data);

      if (frame === null) {
        push({ kind: 'unknown', text: data });

        return;
      }

      if (frame.type === 'ready') {
        state = 'ready';
        resolveReady(frame);
      } else if (frame.type === 'error' && state === 'connecting') {
        rejectReady(new Error(frame.message));
      } else if (frame.type === 'ended') {
        state = 'ended';
      }

      push({ kind: 'frame', frame });

      return;
    }

    if (data instanceof ArrayBuffer) {
      push({ kind: 'audio', data: new Uint8Array(data) });
    } else if (ArrayBuffer.isView(data)) {
      push({ kind: 'audio', data: new Uint8Array(data.buffer, data.byteOffset, data.byteLength) });
    }
  };

  socket.onclose = (event) => {
    state = 'closed';
    rejectReady(new Error(`The socket closed before the conversation was ready (code ${event.code}).`));
    push({ kind: 'closed', code: event.code, reason: event.reason, clean: event.wasClean });
    finish();
  };

  return {
    url,
    ready,

    events(): AsyncIterable<VoiceEvent> {
      if (taken) {
        throw new VoiceConversationStateError('events() can be taken once.');
      }

      taken = true;

      return {
        [Symbol.asyncIterator](): AsyncIterator<VoiceEvent> {
          return {
            next(): Promise<IteratorResult<VoiceEvent>> {
              const next = queue.shift();

              if (next !== undefined) {
                return Promise.resolve({ value: next, done: false });
              }

              if (finished) {
                return Promise.resolve({ value: undefined, done: true });
              }

              return new Promise((resolve) => {
                waiting = resolve;
              });
            },
          };
        },
      };
    },

    sendAudio(chunk): void {
      requireReady('Audio');
      socket.send(chunk);
    },

    commit(): void {
      requireReady('commit');
      send({ type: 'commit' });
    },

    cancel(): void {
      requireReady('cancel');
      send({ type: 'cancel' });
    },

    stop(): void {
      if (socket.readyState === OPEN) {
        send({ type: 'stop' });
      }
    },

    close(): void {
      socket.close(1000);
    },
  };
}

function defaultWebSocket(url: string, protocols: string[]): WebSocketLike {
  const Constructor = (globalThis as { WebSocket?: new (url: string, protocols: string[]) => WebSocketLike }).WebSocket;

  if (Constructor === undefined) {
    throw new Error(
      'This runtime has no global WebSocket. Pass options.webSocket: (url, protocols) => new WebSocket(url, protocols).',
    );
  }

  return new Constructor(url, protocols);
}
