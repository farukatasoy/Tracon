import { describe, expect, it } from 'vitest';
import {
  VOICE_SUBPROTOCOL,
  VOICE_TOKEN_PREFIX,
  createSilenceDetector,
  loudness,
  parseVoiceEvent,
  voiceSubProtocols,
} from './voice';

describe('voiceSubProtocols', () => {
  it('offers only the protocol when no token is set', () => {
    expect(voiceSubProtocols(null)).toEqual([VOICE_SUBPROTOCOL]);
    expect(voiceSubProtocols('')).toEqual([VOICE_SUBPROTOCOL]);
  });

  it('carries the token as a sub-protocol, never in a URL', () => {
    expect(voiceSubProtocols('secret')).toEqual([VOICE_SUBPROTOCOL, `${VOICE_TOKEN_PREFIX}secret`]);
  });
});

describe('createSilenceDetector', () => {
  const detector = () => createSilenceDetector({ threshold: 0.1, hangoverMs: 500, minSpeechMs: 200 });

  it('stays idle while nothing is said', () => {
    const vad = detector();

    expect(vad.push(0.01, 0)).toBe('idle');
    expect(vad.push(0.0, 1000)).toBe('idle');
  });

  it('closes the utterance after the hangover window', () => {
    const vad = detector();

    expect(vad.push(0.5, 0)).toBe('speaking');
    expect(vad.push(0.5, 300)).toBe('speaking');
    expect(vad.push(0.0, 400)).toBe('speaking');
    expect(vad.push(0.0, 800)).toBe('speaking');
    expect(vad.push(0.0, 900)).toBe('ended');
  });

  it('does NOT close on a pause inside a sentence', () => {
    // A comma-length pause must not cut the speaker off mid-thought.
    const vad = detector();

    vad.push(0.5, 0);
    expect(vad.push(0.0, 200)).toBe('speaking');
    expect(vad.push(0.5, 400)).toBe('speaking');
    expect(vad.push(0.5, 600)).toBe('speaking');
  });

  it('ignores a click too short to be speech', () => {
    const vad = detector();

    // Speech lasted 100 ms, below the 200 ms floor.
    vad.push(0.5, 0);
    expect(vad.push(0.0, 100)).toBe('speaking');
    expect(vad.push(0.0, 2000)).toBe('speaking');
  });

  it('starts a fresh utterance after one ends', () => {
    const vad = detector();

    vad.push(0.5, 0);
    vad.push(0.0, 300);
    expect(vad.push(0.0, 900)).toBe('ended');

    expect(vad.push(0.0, 1000)).toBe('idle');
    expect(vad.push(0.5, 1100)).toBe('speaking');
  });

  it('reset forgets the current utterance', () => {
    const vad = detector();

    vad.push(0.5, 0);
    vad.reset();

    expect(vad.push(0.0, 5000)).toBe('idle');
  });
});

describe('loudness', () => {
  it('is zero for silence', () => {
    expect(loudness(new Uint8Array(64).fill(128))).toBe(0);
  });

  it('is one for a full-scale square wave', () => {
    expect(loudness(new Uint8Array([0, 255, 0, 255]))).toBeCloseTo(1, 1);
  });

  it('is zero for an empty block', () => {
    expect(loudness(new Uint8Array(0))).toBe(0);
  });
});

describe('parseVoiceEvent', () => {
  it('reads a well formed frame', () => {
    expect(parseVoiceEvent('{"type":"done","turn":2}')).toEqual({ type: 'done', turn: 2 });
  });

  it('returns null for anything that is not a frame', () => {
    expect(parseVoiceEvent('not json')).toBeNull();
    expect(parseVoiceEvent('[]')).toBeNull();
    expect(parseVoiceEvent('{"noType":1}')).toBeNull();
  });
});
