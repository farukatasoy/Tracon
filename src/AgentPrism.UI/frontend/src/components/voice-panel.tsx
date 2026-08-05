import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import {
  VOICE_INPUT_FORMAT,
  createSilenceDetector,
  loudness,
  microphoneSupport,
  parseVoiceEvent,
  voiceStreamUrl,
  voiceSubProtocols,
} from '../lib/voice';
import { Badge, Button, cx } from './ui';
import { CrossIcon, MicIcon, StopIcon } from './icons';

/** Where a conversation is in its cycle, from the browser's point of view. */
type VoiceState = 'off' | 'connecting' | 'listening' | 'thinking' | 'speaking' | 'failed';

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
  const [state, setState] = useState<VoiceState>('off');
  const [level, setLevel] = useState(0);
  const [turns, setTurns] = useState<VoiceTurn[]>([]);
  const [error, setError] = useState<string | null>(null);
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

        case 'error':
          setError(event.message ?? 'The conversation failed.');
          setState('failed');
          break;

        default:
          break;
      }
    },
    [play],
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
        connection.send(JSON.stringify({ type: 'start', agent, inputFormat: VOICE_INPUT_FORMAT }));
      };

      connection.onmessage = (message: MessageEvent<string | ArrayBuffer>) => {
        if (typeof message.data === 'string') {
          handleEvent(parseVoiceEvent(message.data));
          return;
        }

        incoming.current.push(new Uint8Array(message.data));
      };

      connection.onerror = () => {
        setError('The conversation socket failed. Check that the server allows WebSocket upgrades.');
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

      media.ondataavailable = (event: BlobEvent) => {
        if (event.data.size > 0 && connection.readyState === WebSocket.OPEN) {
          void event.data.arrayBuffer().then((buffer) => connection.send(buffer));
        }
      };

      media.onstop = () => {
        if (connection.readyState === WebSocket.OPEN) {
          connection.send(JSON.stringify({ type: 'commit' }));
        }
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
  }, [agent, handleEvent, sessionId]);

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
      <div className="border-t border-line p-3 text-[12px] text-subtle" data-testid="voice-unsupported">
        Conversation mode is unavailable. {support.reason}
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
          {state === 'off' ? 'Talk' : 'End conversation'}
        </Button>

        {state !== 'off' && (
          <>
            <Meter level={level} active={state === 'listening'} />
            <span className="text-[11px] text-subtle" data-testid="voice-state">
              {state}
            </span>
          </>
        )}

        {state === 'listening' && (
          <Button onClick={commit} testId="voice-commit" title="Send what you said">
            Send now
          </Button>
        )}

        {state === 'speaking' && (
          <Button onClick={interrupt} testId="voice-interrupt">
            <CrossIcon className="size-3.5" />
            Interrupt
          </Button>
        )}

        {persistAudio && (
          <span data-testid="voice-recording-notice">
            <Badge tone="warn">Audio of the reply is being stored</Badge>
          </span>
        )}
      </div>

      {error !== null && <p className="text-[12px] text-danger">{error}</p>}

      {turns.length > 0 && (
        <div className="flex flex-col gap-2 text-[13px]" data-testid="voice-transcript">
          {turns.map((turn) => (
            <div key={turn.id} className="flex flex-col gap-0.5">
              <p className="text-subtle">You: {turn.prompt}</p>
              <p>
                {turn.reply}
                {turn.cancelled && <span className="text-subtle"> (interrupted)</span>}
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
