/**
 * Keyboard shortcut resolution. Pure logic, covered by unit tests.
 *
 * The rules that matter are the ones that keep shortcuts out of the way:
 *  - a keystroke inside a text field belongs to the text field, unless the
 *    binding explicitly asks for it (`Ctrl+Enter`, `Esc`);
 *  - the browser's own chords are never claimed. `Ctrl+K` is the one exception
 *    and it is the convention every console uses;
 *  - a two-key sequence (`g` then `a`) expires, so a stray `g` does not sit
 *    there waiting to swallow the next keystroke.
 */

/** The parts of a `KeyboardEvent` the matcher reads. */
export interface ShortcutEvent {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  altKey: boolean;
}

/** The parts of an event target the matcher reads. */
export interface ShortcutTarget {
  tagName: string;
  isContentEditable: boolean;
}

export interface ShortcutBinding {
  /** `mod+k`, `?`, `/`, `mod+enter`, `escape`, or a sequence such as `g a`. */
  keys: string;
  /** Name reported when the binding fires. */
  action: string;
  /** Fire even when the caret is in a text field. Default `false`. */
  insideText?: boolean;
}

/** How long a sequence prefix (`g`) waits for its second key. */
export const SEQUENCE_TIMEOUT_MS = 1_200;

/**
 * Canonical name for one keystroke.
 *
 * Ctrl and Cmd collapse into `mod`: the same binding has to work on both
 * platforms, and no shortcut here distinguishes them. Shift is not recorded
 * because the character already carries it — `?` IS shift and slash.
 */
export function chordOf(event: ShortcutEvent): string {
  const parts: string[] = [];

  if (event.ctrlKey || event.metaKey) {
    parts.push('mod');
  }

  if (event.altKey) {
    parts.push('alt');
  }

  parts.push(event.key === ' ' ? 'space' : event.key.toLowerCase());

  return parts.join('+');
}

/** True when the keystroke is being typed into a text field. */
export function isTextEntry(target: ShortcutTarget | null | undefined): boolean {
  if (target === null || target === undefined) {
    return false;
  }

  if (target.isContentEditable) {
    return true;
  }

  const tag = target.tagName.toUpperCase();

  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

export interface ShortcutMatcher {
  /** Feeds one keystroke; returns the action it triggered, or `null`. */
  push(event: ShortcutEvent, context: { now: number; inTextEntry: boolean }): string | null;
  /** Forgets a half-typed sequence. */
  reset(): void;
}

export function createShortcutMatcher(
  bindings: readonly ShortcutBinding[],
  timeoutMs: number = SEQUENCE_TIMEOUT_MS,
): ShortcutMatcher {
  const chords = new Map<string, ShortcutBinding>();
  const sequences = new Map<string, ShortcutBinding>();
  const prefixes = new Set<string>();

  for (const binding of bindings) {
    const tokens = binding.keys.trim().toLowerCase().split(/\s+/);

    if (tokens.length === 1) {
      chords.set(tokens[0] as string, binding);

      continue;
    }

    sequences.set(tokens.join(' '), binding);
    prefixes.add(tokens[0] as string);
  }

  let pending: string | null = null;
  let pendingAt = 0;

  const allowed = (binding: ShortcutBinding | undefined, inTextEntry: boolean): string | null => {
    if (binding === undefined) {
      return null;
    }

    return inTextEntry && binding.insideText !== true ? null : binding.action;
  };

  return {
    push(event, context) {
      const chord = chordOf(event);

      if (pending !== null && context.now - pendingAt <= timeoutMs) {
        const action = allowed(sequences.get(`${pending} ${chord}`), context.inTextEntry);

        pending = null;

        if (action !== null) {
          return action;
        }
      }

      pending = null;

      const direct = allowed(chords.get(chord), context.inTextEntry);

      if (direct !== null) {
        return direct;
      }

      // A prefix is only armed outside text fields: `g` typed into a search box
      // is the letter g, and arming it there would eat the next keystroke.
      if (!context.inTextEntry && prefixes.has(chord)) {
        pending = chord;
        pendingAt = context.now;
      }

      return null;
    },

    reset() {
      pending = null;
    },
  };
}
