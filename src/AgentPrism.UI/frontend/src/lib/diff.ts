/**
 * Line- and set-level diffing for version comparison and audit before/after views.
 *
 * No library is used here (decision K-045's rationale extended): a diff library
 * costs 8-20 KB gzipped against a 250 KB budget, and a line-based LCS diff is
 * ~120 lines of pure logic. The server never computes a diff — it returns two
 * raw definitions and the client decides how to present them.
 */

export type DiffLineKind = 'same' | 'added' | 'removed';

export interface DiffLine {
  kind: DiffLineKind;
  text: string;
  leftNo?: number;
  rightNo?: number;
}

export interface DiffResult {
  lines: DiffLine[];
  /** True when the inputs exceeded the LCS size limit and a simpler remove/add fallback was used. */
  truncated: boolean;
}

/**
 * Above this many lines on either side, the O(n*m) LCS table becomes
 * expensive enough to matter in a browser tab. The fallback below trades
 * alignment quality for a bounded, always-fast render.
 */
const MAX_LINES_FOR_LCS = 5_000;

/** Line-by-line diff between two texts. Moved lines show as a remove + add pair, not as a move. */
export function diffLines(left: string, right: string): DiffResult {
  const leftLines = left.length === 0 ? [] : left.split('\n');
  const rightLines = right.length === 0 ? [] : right.split('\n');

  if (leftLines.length > MAX_LINES_FOR_LCS || rightLines.length > MAX_LINES_FOR_LCS) {
    return { lines: diffWithoutAlignment(leftLines, rightLines), truncated: true };
  }

  return { lines: diffWithLcs(leftLines, rightLines), truncated: false };
}

function diffWithLcs(leftLines: string[], rightLines: string[]): DiffLine[] {
  const n = leftLines.length;
  const m = rightLines.length;

  // lengths[i][j] = length of the LCS of leftLines[i:] and rightLines[j:].
  // A flat array with rows of (m + 1) is used instead of number[][] so every
  // cell access below is a provably in-bounds index into a single buffer,
  // which also sidesteps noUncheckedIndexedAccess without assertions.
  const width = m + 1;
  const lengths = new Array<number>((n + 1) * width).fill(0);
  const at = (row: number, col: number) => lengths[(row * width) + col] ?? 0;

  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      const value =
        leftLines[i] === rightLines[j] ? at(i + 1, j + 1) + 1 : Math.max(at(i + 1, j), at(i, j + 1));

      lengths[(i * width) + j] = value;
    }
  }

  const result: DiffLine[] = [];
  let i = 0;
  let j = 0;
  let leftNo = 1;
  let rightNo = 1;

  while (i < n && j < m) {
    const leftLine = leftLines[i] ?? '';
    const rightLine = rightLines[j] ?? '';

    if (leftLine === rightLine) {
      result.push({ kind: 'same', text: leftLine, leftNo, rightNo });
      i += 1;
      j += 1;
      leftNo += 1;
      rightNo += 1;
    } else if (at(i + 1, j) >= at(i, j + 1)) {
      result.push({ kind: 'removed', text: leftLine, leftNo });
      i += 1;
      leftNo += 1;
    } else {
      result.push({ kind: 'added', text: rightLine, rightNo });
      j += 1;
      rightNo += 1;
    }
  }

  while (i < n) {
    result.push({ kind: 'removed', text: leftLines[i] ?? '', leftNo });
    i += 1;
    leftNo += 1;
  }

  while (j < m) {
    result.push({ kind: 'added', text: rightLines[j] ?? '', rightNo });
    j += 1;
    rightNo += 1;
  }

  return result;
}

/** Bounded fallback for oversized inputs: every left line removed, every right line added. */
function diffWithoutAlignment(leftLines: string[], rightLines: string[]): DiffLine[] {
  const result: DiffLine[] = [];

  leftLines.forEach((text, index) => result.push({ kind: 'removed', text, leftNo: index + 1 }));
  rightLines.forEach((text, index) => result.push({ kind: 'added', text, rightNo: index + 1 }));

  return result;
}

export interface SetDiffResult {
  added: string[];
  removed: string[];
  unchanged: string[];
}

/** Set difference between two string lists (order-insensitive), for tool/skill/callable-agent name lists. */
export function diffSets(left: readonly string[], right: readonly string[]): SetDiffResult {
  const leftSet = new Set(left);
  const rightSet = new Set(right);

  return {
    added: right.filter((item) => !leftSet.has(item)),
    removed: left.filter((item) => !rightSet.has(item)),
    unchanged: left.filter((item) => rightSet.has(item)),
  };
}
