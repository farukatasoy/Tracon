import { describe, expect, it } from 'vitest';
import { diffLines, diffSets } from './diff';

describe('diffLines', () => {
  it('marks every line same when both sides are identical', () => {
    const { lines, truncated } = diffLines('a\nb\nc', 'a\nb\nc');

    expect(truncated).toBe(false);
    expect(lines).toEqual([
      { kind: 'same', text: 'a', leftNo: 1, rightNo: 1 },
      { kind: 'same', text: 'b', leftNo: 2, rightNo: 2 },
      { kind: 'same', text: 'c', leftNo: 3, rightNo: 3 },
    ]);
  });

  it('detects a single added line', () => {
    const { lines } = diffLines('a\nc', 'a\nb\nc');

    expect(lines).toEqual([
      { kind: 'same', text: 'a', leftNo: 1, rightNo: 1 },
      { kind: 'added', text: 'b', rightNo: 2 },
      { kind: 'same', text: 'c', leftNo: 2, rightNo: 3 },
    ]);
  });

  it('detects a single removed line', () => {
    const { lines } = diffLines('a\nb\nc', 'a\nc');

    expect(lines).toEqual([
      { kind: 'same', text: 'a', leftNo: 1, rightNo: 1 },
      { kind: 'removed', text: 'b', leftNo: 2 },
      { kind: 'same', text: 'c', leftNo: 3, rightNo: 2 },
    ]);
  });

  it('shows a moved line as a remove and an add, not as a move', () => {
    const { lines } = diffLines('a\nb\nc', 'b\nc\na');

    // No "moved" line kind exists; the LCS is ["b", "c"], and "a" appears
    // once as removed (its old position) and once as added (its new one).
    const kinds = lines.map((line) => line.kind);
    expect(kinds).toContain('removed');
    expect(kinds).toContain('added');
    expect(lines.filter((line) => line.text === 'a')).toHaveLength(2);
  });

  it('handles empty inputs on either side', () => {
    expect(diffLines('', '').lines).toEqual([]);
    expect(diffLines('', 'a').lines).toEqual([{ kind: 'added', text: 'a', rightNo: 1 }]);
    expect(diffLines('a', '').lines).toEqual([{ kind: 'removed', text: 'a', leftNo: 1 }]);
  });

  it('falls back to an unaligned diff and reports truncation past the line limit', () => {
    const big = Array.from({ length: 5_001 }, (_, i) => `line-${i}`).join('\n');

    const { lines, truncated } = diffLines(big, 'a\nb');

    expect(truncated).toBe(true);
    expect(lines.filter((line) => line.kind === 'removed')).toHaveLength(5_001);
    expect(lines.filter((line) => line.kind === 'added')).toHaveLength(2);
  });
});

describe('diffSets', () => {
  it('splits into added, removed and unchanged', () => {
    const result = diffSets(['alpha', 'beta'], ['beta', 'gamma']);

    expect(result.added).toEqual(['gamma']);
    expect(result.removed).toEqual(['alpha']);
    expect(result.unchanged).toEqual(['beta']);
  });

  it('is order-insensitive', () => {
    const result = diffSets(['a', 'b'], ['b', 'a']);

    expect(result.added).toEqual([]);
    expect(result.removed).toEqual([]);
    expect(result.unchanged).toEqual(['a', 'b']);
  });

  it('handles empty lists', () => {
    expect(diffSets([], [])).toEqual({ added: [], removed: [], unchanged: [] });
    expect(diffSets([], ['a'])).toEqual({ added: ['a'], removed: [], unchanged: [] });
    expect(diffSets(['a'], [])).toEqual({ added: [], removed: ['a'], unchanged: [] });
  });
});
