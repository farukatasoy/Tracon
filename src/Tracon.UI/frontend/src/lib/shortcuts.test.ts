import { describe, expect, it } from 'vitest';
import { chordOf, createShortcutMatcher, isTextEntry, type ShortcutBinding } from './shortcuts';

const key = (value: string, modifiers: Partial<{ ctrl: boolean; meta: boolean; alt: boolean }> = {}) => ({
  key: value,
  ctrlKey: modifiers.ctrl === true,
  metaKey: modifiers.meta === true,
  altKey: modifiers.alt === true,
});

const BINDINGS: readonly ShortcutBinding[] = [
  { keys: 'mod+k', action: 'palette', insideText: true },
  { keys: 'escape', action: 'close', insideText: true },
  { keys: '?', action: 'help' },
  { keys: '/', action: 'search' },
  { keys: 'g a', action: 'go:agents' },
  { keys: 'g r', action: 'go:runs' },
];

const outside = { now: 0, inTextEntry: false };

describe('chordOf', () => {
  it('collapses Ctrl and Cmd into one name', () => {
    expect(chordOf(key('k', { ctrl: true }))).toBe('mod+k');
    expect(chordOf(key('k', { meta: true }))).toBe('mod+k');
  });

  it('lowercases the key so Shift does not change the chord', () => {
    expect(chordOf(key('K', { ctrl: true }))).toBe('mod+k');
  });

  it('names a named key in lower case', () => {
    expect(chordOf(key('Escape'))).toBe('escape');
    expect(chordOf(key('ArrowDown'))).toBe('arrowdown');
  });

  it('names the space bar', () => {
    expect(chordOf(key(' '))).toBe('space');
  });

  it('records Alt', () => {
    expect(chordOf(key('j', { alt: true }))).toBe('alt+j');
  });
});

describe('isTextEntry', () => {
  it('recognises the form controls that own their keystrokes', () => {
    expect(isTextEntry({ tagName: 'INPUT', isContentEditable: false })).toBe(true);
    expect(isTextEntry({ tagName: 'textarea', isContentEditable: false })).toBe(true);
    expect(isTextEntry({ tagName: 'SELECT', isContentEditable: false })).toBe(true);
  });

  it('recognises a contenteditable element', () => {
    expect(isTextEntry({ tagName: 'DIV', isContentEditable: true })).toBe(true);
  });

  it('reports anything else as not a text entry', () => {
    expect(isTextEntry({ tagName: 'BUTTON', isContentEditable: false })).toBe(false);
    expect(isTextEntry(null)).toBe(false);
    expect(isTextEntry(undefined)).toBe(false);
  });
});

describe('createShortcutMatcher', () => {
  it('matches a single chord', () => {
    const matcher = createShortcutMatcher(BINDINGS);

    expect(matcher.push(key('k', { meta: true }), outside)).toBe('palette');
  });

  it('matches a two-key sequence', () => {
    const matcher = createShortcutMatcher(BINDINGS);

    expect(matcher.push(key('g'), outside)).toBeNull();
    expect(matcher.push(key('a'), { now: 200, inTextEntry: false })).toBe('go:agents');
  });

  it('forgets a sequence prefix after the timeout', () => {
    const matcher = createShortcutMatcher(BINDINGS, 1_000);

    expect(matcher.push(key('g'), outside)).toBeNull();
    expect(matcher.push(key('r'), { now: 5_000, inTextEntry: false })).toBeNull();
  });

  it('does not leave a prefix armed after a failed sequence', () => {
    const matcher = createShortcutMatcher(BINDINGS);

    matcher.push(key('g'), outside);

    // `g x` is not a binding, and the `x` must not stay half-typed.
    expect(matcher.push(key('x'), { now: 100, inTextEntry: false })).toBeNull();
    expect(matcher.push(key('a'), { now: 200, inTextEntry: false })).toBeNull();
  });

  it('re-arms a prefix that follows itself', () => {
    const matcher = createShortcutMatcher(BINDINGS);

    matcher.push(key('g'), outside);

    expect(matcher.push(key('g'), { now: 100, inTextEntry: false })).toBeNull();
    expect(matcher.push(key('r'), { now: 200, inTextEntry: false })).toBe('go:runs');
  });

  it('returns null for an unbound chord', () => {
    const matcher = createShortcutMatcher(BINDINGS);

    expect(matcher.push(key('z'), outside)).toBeNull();
  });

  it('reset forgets a half-typed sequence', () => {
    const matcher = createShortcutMatcher(BINDINGS);

    matcher.push(key('g'), outside);
    matcher.reset();

    expect(matcher.push(key('a'), { now: 100, inTextEntry: false })).toBeNull();
  });

  describe('inside a text field', () => {
    const inside = { now: 0, inTextEntry: true };

    it('does NOT fire a plain-letter shortcut', () => {
      const matcher = createShortcutMatcher(BINDINGS);

      expect(matcher.push(key('/'), inside)).toBeNull();
      expect(matcher.push(key('?'), inside)).toBeNull();
    });

    it('does NOT arm a sequence prefix', () => {
      const matcher = createShortcutMatcher(BINDINGS);

      matcher.push(key('g'), inside);

      // The `a` belongs to the text, not to a half-typed `g a`.
      expect(matcher.push(key('a'), { now: 100, inTextEntry: true })).toBeNull();
      expect(matcher.push(key('a'), { now: 200, inTextEntry: false })).toBeNull();
    });

    it('still fires a binding that opted in', () => {
      const matcher = createShortcutMatcher(BINDINGS);

      expect(matcher.push(key('k', { ctrl: true }), inside)).toBe('palette');
      expect(matcher.push(key('Escape'), inside)).toBe('close');
    });
  });
});
