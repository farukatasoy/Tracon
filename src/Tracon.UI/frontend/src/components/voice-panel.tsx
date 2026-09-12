import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useLocale, useT, type MessageKey } from '../lib/i18n';
import {
  VOICE_INPUT_FORMAT,
  createSilenceDetector,
  loudness,
  microphoneSupport,
  parseVoiceEvent,
  readVoiceForLocale,
  voiceStreamUrl,
  voiceSubProtocols,
} from '../lib/voice';
import { Badge, Button, cx } from './ui';
import { CrossIcon, MicIcon, StopIcon } from './icons';

/** Where a conversation is in its cycle, from the browser's point of view. */
type VoiceState = 'off' | 'connecting' | 'listening' | 'thinking' | 'speaking' | 'failed';

const VOICE_STATE_LABEL: Record<VoiceState, MessageKey> = {
  off: 'voice.state.off',
  connecting: 'voice.state.connecting',
  listening: 'voice.state.listening',
  thinking: 'voice.state.thinking',
  speaking: 'voice.state.speaking',
  failed: 'voice.state.failed',
};

interface VoiceTurn {
  id: number;
  prompt: string;
  reply: string;
  runId: string | null;
  cancelled: boolean;
}

/**
 * Real-time conversation with an agent.
 *
 * ⚠️ This panel changes how the page talks to the server: a WebSocket stays
 * open for the whole conversation instead of one request per turn. It is opened
 * only when the user presses the microphone button.
 *
 * The pipeline is the server's (option A): speech goes up, the normal streaming
 * run happens on the server, and audio comes back sentence by sentence. The
 * browser only captures, plays and decides when the speaker stopped.
 *
 * 🚨 Recording is stopped while the agent answers. A microphone left open would
 * capture the reply coming out of the speakers and feed it back as the next
 * question; interruption is therefore an explicit button, not a sound level.
 */
export function VoicePanel({ agent, sessionId }: { agent: string; sessionId: string }): ReactNode {
  // 🚨 `t` is the module-level function and NEVER changes identity, so it can sit
  // in the dependency array of `start` without rebuilding it — a rebuild here
  // would tear down the open WebSocket mid-conversation.
  const t = useT();
  const { locale } = useLocale();
  const [state, setState] = useState<VoiceState>('off');
  const [level, setLevel] = useState(0);
  const [turns, setTurns] = useState<VoiceTurn[]>([]);
  const [error, setError] = useState<MessageKey | string | null>(null);
  const [persistAudio, setPersistAudio] = useState(false);

  const socket = useRef<WebSocket | null>(null);
  const recorder = useRef<MediaRecorder | null>(null);
  const stream = useRef<MediaStream | null>(null);
  const audioContext = useRef<AudioContext | null>(null);
  const meter = useRef<number | null>(null);
  const playing = useRef<AudioBufferSourceNode | null>(null);
  const incoming = useRef<Uint8Array[]>([]);
  const turnId = useRef(0);

  const support = microphoneSupport();

  /** Tears everything down. Safe to call twice. */
  const stop = useCallback(() => {
    if (meter.current !== null) {
      cancelAnimationFrame(meter.current);
      meter.current = null;
    }

    playing.current?.stop();
    playing.current = null;

    recorder.current?.state === 'recording' && recorder.current.stop();
    recorder.current = null;

    for (const track of stream.current?.getTracks() ?? []) {
      track.stop();
    }

    stream.current = null;

    void audioContext.current?.close();
    audioContext.current = null;

    if (socket.current !== null && socket.current.readyState <= WebSocket.OPEN) {
      socket.current.send(JSON.stringify({ type: 'stop' }));
      socket.current.close();
    }

    socket.current = null;
    setLevel(0);
    setState('off');
  }, []);

  useEffect(() => stop, [stop]);

  /** Plays one finished audio segment, queued behind the previous one. */
  const play = useCallback(async (chunks: Uint8Array[]) => {
    const context = audioContext.current;

    if (context === null || chunks.length === 0) {
      return;
    }

    const total = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
    const merged = new Uint8Array(total);
    let offset = 0;

    for (const chunk of chunks) {
      merged.set(chunk, offset);
      offset += chunk.length;
    }

    // A segment is a complete file, so `decodeAudioData` works everywhere.
    // Feeding a partial stream would need Media Source Extensions, whose
    // support for raw audio containers is uneven across browsers.
    const buffer = await context.decodeAudioData(merged.buffer as ArrayBuffer);
    const source = context.createBufferSource();

    source.buffer = buffer;
    source.connect(context.destination);
    source.start();

    playing.current = source;
  }, []);

  const handleEvent = useCallback(
    (event: ReturnType<typeof parseVoiceEvent>) => {
      if (event === null) {
        return;
      }

      switch (event.type) {
        case 'ready':
          setPersistAudio(event.persistAudio === true);
          setState('listening');
          break;

        case 'transcript':
          if ((event.text ?? '').length > 0) {
            turnId.current += 1;
            setTurns((current) => [
              ...current,
              { id: turnId.current, prompt: event.text ?? '', reply: '', runId: null, cancelled: false },
            ]);
            setState('thinking');
          }

          break;

        case 'runStarted':
          setTurns((current) =>
            current.map((turn, index) =>
              index === current.length - 1 ? { ...turn, runId: event.runId ?? null } : turn,
            ),
          );
          break;

        case 'text':
          setTurns((current) =>
            current.map((turn, index) =>
              index === current.length - 1 ? { ...turn, reply: turn.reply + (event.delta ?? '') } : turn,
            ),
          );
          break;

        case 'audioStart':
          incoming.current = [];
          setState('speaking');
          break;

        case 'audioEnd':
          void play(incoming.current);
          incoming.current = [];
          break;

        case 'done':
          setTurns((current) =>
            current.map((turn, index) =>
              index === current.length - 1 ? { ...turn, cancelled: event.cancelled === true } : turn,
            ),
          );

          setState('listening');
          recorder.current?.state === 'inactive' && recorder.current.start(250);
          break;

        case 'idle':
          // 🚨 The commit closed nothing - it reached the server before any audio.
          // Pressing send inside the recorder's first timeslice does this. We are
          // already in 'thinking' because `commit` put us there, and the recorder
          // is stopped, so without this the panel hangs and the microphone never
          // reopens. Deliberately NOT folded into 'done': no turn ran, so the
          // previous turn's record must not be touched.
          setState('listening');
          recorder.current?.state === 'inactive' && recorder.current.start(250);
          break;

        case 'error':
          // Server text, shown as it came: the API contract is single-language.
          setError(event.message ?? t('voice.failed'));
          setState('failed');
          break;

        default:
          break;
      }
    },
    [play, t],
  );

  /** Opens the socket, the microphone and the meter. */
  const start = useCallback(async () => {
    setError(null);
    setState('connecting');

    try {
      const captured = await navigator.mediaDevices.getUserMedia({
        // Noise suppression and echo cancellation are left to the browser:
        // doing them on the server would mean shipping signal processing.
        audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 },
      });

      stream.current = captured;

      const context = new AudioContext();
      audioContext.current = context;

      const analyser = context.createAnalyser();
      analyser.fftSize = 1024;
      context.createMediaStreamSource(captured).connect(analyser);

      const connection = new WebSocket(voiceStreamUrl(sessionId), voiceSubProtocols());
      connection.binaryType = 'arraybuffer';
      socket.current = connection;

      connection.onopen = () => {
        // The voice is chosen HERE, by language. The protocol already carries
        // `voiceId` in the start frame, so the server needs no notion of a
        // language at all. An unset preference falls back to the server default.
        const voiceId = readVoiceForLocale(locale);

        connection.send(
          JSON.stringify({
            type: 'start',
            agent,
            inputFormat: VOICE_INPUT_FORMAT,
            ...(voiceId === null ? {} : { voiceId }),
          }),
        );
      };

      connection.onmessage = (message: MessageEvent<string | ArrayBuffer>) => {
        if (typeof message.data === 'string') {
          handleEvent(parseVoiceEvent(message.data));
          return;
        }

        incoming.current.push(new Uint8Array(message.data));
      };

      connection.onerror = () => {
        setError(t('voice.socketFailed'));
        setState('failed');
      };

      connection.onclose = () => {
        setState((current) => (current === 'failed' ? current : 'off'));
      };

      // 🚨 The recorder is restarted for every utterance. A time-sliced
      // MediaRecorder writes the container header into the FIRST chunk only,
      // so a second utterance taken from the same recording would arrive
      // headerless and could not be decoded.
      const media = new MediaRecorder(captured, { mimeType: 'audio/webm;codecs=opus' });
      recorder.current = media;

      // 🚨 Every send is chained onto this promise. `Blob.arrayBuffer()` resolves
      // asynchronously, so unchained sends have two failure modes: chunks can
      // reach the socket out of order and corrupt the WebM stream, and the
      // `commit` frame below can overtake the audio it commits — the server
      // then finds an empty buffer and drops the turn silently
      // (VoiceConversationDriver.Commit).
      let pendingSends: Promise<unknown> = Promise.resolve();

      media.ondataavailable = (event: BlobEvent) => {
        if (event.data.size > 0 && connection.readyState === WebSocket.OPEN) {
          pendingSends = pendingSends
            .then(() => event.data.arrayBuffer())
            .then((buffer) => {
              if (connection.readyState === WebSocket.OPEN) {
                connection.send(buffer);
              }
            });
        }
      };

      media.onstop = () => {
        void pendingSends.then(() => {
          if (connection.readyState === WebSocket.OPEN) {
            connection.send(JSON.stringify({ type: 'commit' }));
          }
        });
      };

      media.start(250);

      const detector = createSilenceDetector();
      const samples = new Uint8Array(analyser.fftSize);

      const tick = () => {
        analyser.getByteTimeDomainData(samples);

        const value = loudness(samples);
        setLevel(value);

        if (detector.push(value, performance.now()) === 'ended' && media.state === 'recording') {
          // Stopping flushes the last chunk; `onstop` then commits.
          media.stop();
          setState('thinking');
        }

        meter.current = requestAnimationFrame(tick);
      };

      meter.current = requestAnimationFrame(tick);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught));
      setState('failed');
    }
  }, [agent, handleEvent, locale, sessionId, t]);

  /**
   * Closes the current utterance by hand.
   *
   * Silence detection is the normal path, but it cannot work everywhere: a
   * noisy room never goes quiet, and a user who wants push-to-talk should not
   * have to wait for a pause. Stopping the recorder flushes the last chunk and
   * `onstop` commits, exactly as the detector would have done.
   */
  const commit = useCallback(() => {
    const media = recorder.current;

    if (media?.state === 'recording') {
      media.stop();
      setState('thinking');
    }
  }, []);

  /** Interrupts the agent mid-answer. */
  const interrupt = useCallback(() => {
    playing.current?.stop();
    playing.current = null;

    socket.current?.send(JSON.stringify({ type: 'cancel' }));
  }, []);

  if (!support.supported) {
    return (
      <div className="border-t border-line p-3 text-sm text-subtle" data-testid="voice-unsupported">
        {t('voice.unavailable')} {support.reason === undefined ? '' : t(support.reason)}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2 border-t border-line p-3">
      <div className="flex items-center gap-2">
        <Button
          tone={state === 'off' ? 'primary' : 'danger'}
          onClick={() => (state === 'off' ? void start() : stop())}
          busy={state === 'connecting'}
          testId="voice-toggle"
        >
          {state === 'off' ? <MicIcon className="size-3.5" /> : <StopIcon className="size-3.5" />}
          {state === 'off' ? t('voice.talk') : t('voice.end')}
        </Button>

        {state !== 'off' && (
          <>
            <Meter level={level} active={state === 'listening'} />
            <span className="text-xs text-subtle" data-testid="voice-state" aria-live="polite">
              {t(VOICE_STATE_LABEL[state])}
            </span>
          </>
        )}

        {state === 'listening' && (
          <Button onClick={commit} testId="voice-commit" title={t('voice.sendNowTitle')}>
            {t('voice.sendNow')}
          </Button>
        )}

        {state === 'speaking' && (
          <Button onClick={interrupt} testId="voice-interrupt">
            <CrossIcon className="size-3.5" />
            {t('voice.interrupt')}
          </Button>
        )}

        {persistAudio && (
          <span data-testid="voice-recording-notice">
            <Badge tone="warn">{t('voice.audioStored')}</Badge>
          </span>
        )}
      </div>

      {error !== null && (
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}

      {turns.length > 0 && (
        <div
          className="flex flex-col gap-2 text-base"
          data-testid="voice-transcript"
          aria-live="polite"
        >
          {turns.map((turn) => (
            <div key={turn.id} className="flex flex-col gap-0.5">
              <p className="text-subtle">{t('voice.you', { text: turn.prompt })}</p>
              <p>
                {turn.reply}
                {turn.cancelled && <span className="text-subtle"> ({t('voice.interrupted')})</span>}
              </p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** Twelve bars that follow the microphone level. */
function Meter({ level, active }: { level: number; active: boolean }): ReactNode {
  const lit = Math.min(12, Math.round(level * 40));

  return (
    <div className="flex items-end gap-0.5" aria-hidden="true" data-testid="voice-meter">
      {Array.from({ length: 12 }, (_, index) => (
        <span
          key={index}
          className={cx(
            'w-0.5 rounded-full transition-[height]',
            index < lit && active ? 'bg-accent' : 'bg-line',
          )}
          style={{ height: `${4 + index}px` }}
        />
      ))}
    </div>
  );
}
