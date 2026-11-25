import { describe, expect, it } from 'vitest';
import {
  createVoiceConversation,
  parseVoiceFrame,
  VOICE_SUBPROTOCOL,
  VoiceConversationStateError,
  voiceStreamUrl,
  voiceSubProtocols,
  type VoiceEvent,
  type WebSocketLike,
} from '../src/index.js';

/** A socket that records what the client does and lets a test play the server. */
class FakeSocket implements WebSocketLike {
  binaryType = 'blob';
  readyState = 0;
  readonly sent: (string | ArrayBufferLike | ArrayBufferView)[] = [];
  closedWith: number | undefined;
  onopen: ((event: unknown) => void) | null = null;
  onmessage: ((event: { data: unknown }) => void) | null = null;
  onerror: ((event: unknown) => void) | null = null;
  onclose: ((event: { code: number; reason: string; wasClean: boolean }) => void) | null = null;

  constructor(
    readonly url: string,
    readonly protocols: string[],
  ) {}

  send(data: string | ArrayBufferLike | ArrayBufferView): void {
    this.sent.push(data);
  }

  close(code?: number): void {
    this.closedWith = code;
    this.readyState = 3;
  }

  /** The server accepts the handshake. */
  open(): void {
    this.readyState = 1;
    this.onopen?.({});
  }

  /** The server writes a text frame. */
  frame(frame: object): void {
    this.onmessage?.({ data: JSON.stringify(frame) });
  }

  /** The server writes a binary frame. */
  audio(bytes: number[]): void {
    this.onmessage?.({ data: new Uint8Array(bytes).buffer });
  }

  /** The socket closes. */
  shut(code = 1000, reason = 'The conversation closed.', wasClean = true): void {
    this.readyState = 3;
    this.onclose?.({ code, reason, wasClean });
  }

  /** The JSON frames the client sent, in order. */
  get sentFrames(): unknown[] {
    return this.sent.filter((data): data is string => typeof data === 'string').map((data) => JSON.parse(data) as unknown);
  }
}

function conversation(options: Partial<Parameters<typeof createVoiceConversation>[0]> = {}) {
  let socket!: FakeSocket;
  const created = createVoiceConversation({
    baseUrl: 'https://example.com/tracon',
    sessionId: 'lesson-42',
    agent: 'coach',
    webSocket: (url, protocols) => (socket = new FakeSocket(url, protocols)),
    ...options,
  });

  return { conversation: created, socket };
}

async function collect(events: AsyncIterable<VoiceEvent>): Promise<VoiceEvent[]> {
  const all: VoiceEvent[] = [];

  for await (const event of events) {
    all.push(event);
  }

  return all;
}

describe('the address and the handshake', () => {
  it('builds a wss address under the MapTracon prefix and encodes the session', () => {
    expect(voiceStreamUrl('https://example.com/tracon', 'a b/c')).toBe(
      'wss://example.com/tracon/api/voice/sessions/a%20b%2Fc/stream',
    );
    expect(voiceStreamUrl('http://localhost:5080/control/', 'one')).toBe(
      'ws://localhost:5080/control/api/voice/sessions/one/stream',
    );
  });

  it('offers only the protocol without a token and the token as a second subprotocol with one', () => {
    expect(voiceSubProtocols()).toEqual([VOICE_SUBPROTOCOL]);
    expect(voiceSubProtocols('')).toEqual([VOICE_SUBPROTOCOL]);
    expect(voiceSubProtocols('secret')).toEqual([VOICE_SUBPROTOCOL, 'tracon.token.secret']);
  });

  it('never puts the token in the address', () => {
    const { conversation: created, socket } = conversation({ token: 'secret-token' });

    expect(socket.url).not.toContain('secret-token');
    expect(created.url).not.toContain('secret-token');
    expect(socket.protocols).toEqual([VOICE_SUBPROTOCOL, 'tracon.token.secret-token']);
  });

  it('reads the token from a function at the time the socket opens', () => {
    const { socket } = conversation({ token: () => 'fresh' });

    expect(socket.protocols).toEqual([VOICE_SUBPROTOCOL, 'tracon.token.fresh']);
  });

  it('sends start as soon as the socket is open, with only the fields it was given', () => {
    const { socket } = conversation({ voiceId: 'v-1', inputFormat: 'pcm16' });

    expect(socket.sentFrames).toEqual([]);

    socket.open();

    expect(socket.sentFrames).toEqual([{ type: 'start', agent: 'coach', voiceId: 'v-1', inputFormat: 'pcm16' }]);
    expect(socket.binaryType).toBe('arraybuffer');

    const bare = conversation();
    bare.socket.open();

    expect(bare.socket.sentFrames).toEqual([{ type: 'start', agent: 'coach' }]);
  });

  it('fails with a message that names the way out when the runtime has no WebSocket', () => {
    const original = (globalThis as { WebSocket?: unknown }).WebSocket;

    (globalThis as { WebSocket?: unknown }).WebSocket = undefined;

    try {
      expect(() => createVoiceConversation({ baseUrl: 'https://x.test/tracon', sessionId: 's', agent: 'a' })).toThrow(
        /options\.webSocket/,
      );
    } finally {
      (globalThis as { WebSocket?: unknown }).WebSocket = original;
    }
  });
});

describe('frames', () => {
  it('reads every frame type the protocol defines and ignores what it does not know', () => {
    expect(parseVoiceFrame('{"type":"idle"}')).toEqual({ type: 'idle' });
    expect(parseVoiceFrame('{"type":"done","cancelled":false,"turn":2}')).toEqual({ type: 'done', cancelled: false, turn: 2 });
    expect(parseVoiceFrame('{"type":"ended","reason":"agent"}')).toEqual({ type: 'ended', reason: 'agent' });
    expect(parseVoiceFrame('{"type":"from-the-future"}')).toBeNull();
    expect(parseVoiceFrame('not json')).toBeNull();
    expect(parseVoiceFrame('[1]')).toBeNull();
    expect(parseVoiceFrame('null')).toBeNull();
    expect(parseVoiceFrame('{"type":7}')).toBeNull();
  });

  it('yields frames, audio chunks and unknown text in the order they arrived, then the close', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    socket.frame({ type: 'ready', agent: 'coach', sessionId: 'lesson-42', persistAudio: false });
    socket.frame({ type: 'transcript', text: 'hello', final: true });
    socket.frame({ type: 'audioStart', mediaType: 'audio/mpeg' });
    socket.audio([1, 2, 3]);
    socket.frame({ type: 'audioEnd' });
    socket.onmessage?.({ data: '{"type":"from-the-future"}' });
    socket.frame({ type: 'done', cancelled: false, turn: 1 });
    socket.shut();

    const events = await collect(created.events());

    expect(events.map((event) => (event.kind === 'frame' ? event.frame.type : event.kind))).toEqual([
      'ready',
      'transcript',
      'audioStart',
      'audio',
      'audioEnd',
      'unknown',
      'done',
      'closed',
    ]);

    const audio = events.find((event) => event.kind === 'audio');

    expect(audio?.kind === 'audio' && Array.from(audio.data)).toEqual([1, 2, 3]);
    expect(events.at(-1)).toEqual({ kind: 'closed', code: 1000, reason: 'The conversation closed.', clean: true });
  });

  it('keeps what arrives before events() is taken and waits for what has not arrived yet', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    socket.frame({ type: 'idle' });

    const iterator = created.events()[Symbol.asyncIterator]();
    const first = await iterator.next();

    expect(first.value).toEqual({ kind: 'frame', frame: { type: 'idle' } });

    const second = iterator.next();

    socket.frame({ type: 'audioEnd' });

    expect((await second).value).toEqual({ kind: 'frame', frame: { type: 'audioEnd' } });
  });

  it('allows events() once', () => {
    const { conversation: created } = conversation();

    created.events();

    expect(() => created.events()).toThrow(VoiceConversationStateError);
  });

  it('treats idle as its own frame: not an error, not a done, and the conversation stays usable', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    socket.frame({ type: 'ready', agent: 'coach', sessionId: 's', persistAudio: false });
    await created.ready;
    created.commit();
    socket.frame({ type: 'idle' });

    expect(() => created.commit()).not.toThrow();
    expect(socket.sentFrames.filter((frame) => (frame as { type: string }).type === 'commit')).toHaveLength(2);
  });
});

describe('what the client may send, and when', () => {
  it('refuses audio, commit and cancel before ready and says what to await', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();

    expect(() => created.sendAudio(new Uint8Array([1]))).toThrow(/await conversation\.ready/);
    expect(() => created.commit()).toThrow(VoiceConversationStateError);
    expect(() => created.cancel()).toThrow(VoiceConversationStateError);

    socket.frame({ type: 'ready', agent: 'coach', sessionId: 's', persistAudio: true });

    const ready = await created.ready;

    expect(ready.persistAudio).toBe(true);

    created.sendAudio(new Uint8Array([9, 8]));
    created.commit();
    created.cancel();

    expect(socket.sent.slice(1).map((data) => (typeof data === 'string' ? (JSON.parse(data) as { type: string }).type : 'audio'))).toEqual([
      'audio',
      'commit',
      'cancel',
    ]);
  });

  it('refuses everything after the server ended the conversation, and says it ended', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    socket.frame({ type: 'ready', agent: 'coach', sessionId: 's', persistAudio: false });
    await created.ready;
    socket.frame({ type: 'done', cancelled: false, turn: 1 });
    socket.frame({ type: 'ended', reason: 'agent' });

    expect(() => created.sendAudio(new Uint8Array([1]))).toThrow(/ended/);
    expect(() => created.commit()).toThrow(VoiceConversationStateError);
  });

  it('sends stop only while the socket is open', () => {
    const { conversation: created, socket } = conversation();

    created.stop();
    expect(socket.sentFrames).toEqual([]);

    socket.open();
    created.stop();
    expect(socket.sentFrames.at(-1)).toEqual({ type: 'stop' });

    socket.shut();
    created.stop();
    expect(socket.sentFrames.filter((frame) => (frame as { type: string }).type === 'stop')).toHaveLength(1);
  });

  it('closes the socket normally on close()', () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    created.close();

    expect(socket.closedWith).toBe(1000);
  });
});

describe('failure before the conversation is ready', () => {
  it('rejects ready with the error frame text', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    socket.frame({ type: 'error', message: 'There is no agent named coach.' });

    await expect(created.ready).rejects.toThrow('There is no agent named coach.');
  });

  it('rejects ready when the socket closes first, and a caller that never awaits it sees no unhandled rejection', async () => {
    const { conversation: created, socket } = conversation();
    const orphan = conversation();

    socket.shut(1006, '', false);
    orphan.socket.shut(1006, '', false);

    await expect(created.ready).rejects.toThrow(/code 1006/);

    const events = await collect(created.events());

    expect(events).toEqual([{ kind: 'closed', code: 1006, reason: '', clean: false }]);
  });

  it('an error after ready is a frame like any other and does not reject anything', async () => {
    const { conversation: created, socket } = conversation();

    socket.open();
    socket.frame({ type: 'ready', agent: 'coach', sessionId: 's', persistAudio: false });
    await created.ready;
    socket.frame({ type: 'error', message: 'The voice turn failed.' });
    socket.frame({ type: 'done', cancelled: false, turn: 1 });
    socket.shut();

    const events = await collect(created.events());

    expect(events.map((event) => (event.kind === 'frame' ? event.frame.type : event.kind))).toEqual([
      'ready',
      'error',
      'done',
      'closed',
    ]);
  });
});
