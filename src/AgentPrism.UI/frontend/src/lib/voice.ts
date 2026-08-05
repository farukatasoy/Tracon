import { apiBase } from './base';
import { getToken } from './auth';

/** WebSocket sub-protocol the server echoes back on a successful handshake. */
export const VOICE_SUBPROTOCOL = 'agentprism.voice.v1';

/** Prefix that carries the bearer token through the handshake. */
export const VOICE_TOKEN_PREFIX = 'agentprism.token.';

/** Audio format this client captures. */
export const VOICE_INPUT_FORMAT = 'webm-opus';

/** Server-to-client event frame. Fields are per-event; most are absent. */
export interface VoiceServerEvent {
  type: string;
  agent?: string;
  sessionId?: string;
  persistAudio?: boolean;
  text?: string;
  final?: boolean;
  runId?: string;
  delta?: string;
  mediaType?: string;
  attachmentId?: string;
  cancelled?: boolean;
  turn?: number;
  message?: string;
}

/**
 * Builds the WebSocket address for a conversation.
 *
 * AgentPrism can be mounted under any prefix, so the address is derived from
 * the same base the REST calls use. The scheme follows the page: `https` pages
 * must use `wss`, or the browser blocks the upgrade as mixed content.
 */
export function voiceStreamUrl(sessionId: string): string {
  const url = new URL(`${apiBase}api/voice/sessions/${encodeURIComponent(sessionId)}/stream`, location.href);

  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';

  return url.toString();
}

/**
 * Sub-protocols to offer during the handshake.
 *
 * The token travels here and NOT in the query string: a URL is written to
 * server logs, reverse-proxy logs and browser history. A browser cannot add an
 * `Authorization` header to a WebSocket upgrade, so this is the standard way
 * out.
 */
export function voiceSubProtocols(token: string | null = getToken()): string[] {
  return token === null || token.length === 0
    ? [VOICE_SUBPROTOCOL]
    : [VOICE_SUBPROTOCOL, VOICE_TOKEN_PREFIX + token];
}

/** Why the microphone cannot be used, if it cannot. */
export interface MicrophoneSupport {
  supported: boolean;
  reason?: string;
}

/**
 * Reports whether this browser and page can capture the microphone.
 *
 * Microphone access requires a SECURE CONTEXT (HTTPS or localhost). A remote
 * install served over plain HTTP cannot use conversation mode at all, and the
 * UI says so instead of failing silently when the button is pressed.
 */
export function microphoneSupport(): MicrophoneSupport {
  if (typeof window === 'undefined' || window.isSecureContext !== true) {
    return {
      supported: false,
      reason:
        'Microphone access needs a secure context. Serve AgentPrism over HTTPS, or open it on localhost.',
    };
  }

  if (navigator.mediaDevices?.getUserMedia === undefined) {
    return { supported: false, reason: 'This browser does not expose getUserMedia.' };
  }

  if (typeof MediaRecorder === 'undefined') {
    return { supported: false, reason: 'This browser does not support MediaRecorder.' };
  }

  return { supported: true };
}

/** What the silence detector concluded about the latest sample. */
export type SpeechPhase = 'idle' | 'speaking' | 'ended';

export interface SilenceDetectorOptions {
  /** Loudness (0..1 RMS) above which the sample counts as speech. */
  threshold?: number;
  /** Silence needed after speech before the utterance is closed, in ms. */
  hangoverMs?: number;
  /** Speech needed before silence can close an utterance, in ms. */
  minSpeechMs?: number;
}

/**
 * Client-side end-of-speech detection.
 *
 * 🚨 Voice activity detection lives HERE, not on the server: server-side VAD
 * means signal processing on the server and a much larger surface. The server
 * keeps only a duration safety net.
 *
 * The function is pure — it takes a loudness reading and a timestamp and
 * returns a phase — so its behaviour is unit tested without a microphone.
 */
export function createSilenceDetector(options: SilenceDetectorOptions = {}) {
  const threshold = options.threshold ?? 0.02;
  const hangoverMs = options.hangoverMs ?? 900;
  const minSpeechMs = options.minSpeechMs ?? 300;

  let speakingSince: number | null = null;
  let silentSince: number | null = null;

  return {
    /** Feeds one loudness reading. */
    push(level: number, now: number): SpeechPhase {
      if (level >= threshold) {
        speakingSince ??= now;
        silentSince = null;

        return 'speaking';
      }

      if (speakingSince === null) {
        return 'idle';
      }

      // A short pause inside a sentence must not close the utterance, so
      // silence has to persist for the whole hangover window.
      silentSince ??= now;

      const spoke = silentSince - speakingSince >= minSpeechMs;
      const quiet = now - silentSince >= hangoverMs;

      if (!spoke || !quiet) {
        return 'speaking';
      }

      speakingSince = null;
      silentSince = null;

      return 'ended';
    },

    /** Forgets the current utterance without reporting an end. */
    reset(): void {
      speakingSince = null;
      silentSince = null;
    },
  };
}

/** Root-mean-square loudness of a time-domain sample block, in 0..1. */
export function loudness(samples: Uint8Array): number {
  let sum = 0;

  for (const sample of samples) {
    const centred = (sample - 128) / 128;
    sum += centred * centred;
  }

  return samples.length === 0 ? 0 : Math.sqrt(sum / samples.length);
}

/** Parses a server frame; returns null when the payload is not a frame. */
export function parseVoiceEvent(data: string): VoiceServerEvent | null {
  try {
    const parsed: unknown = JSON.parse(data);

    return typeof parsed === 'object' && parsed !== null && typeof (parsed as VoiceServerEvent).type === 'string'
      ? (parsed as VoiceServerEvent)
      : null;
  } catch {
    return null;
  }
}
