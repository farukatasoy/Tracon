/**
 * Pure layout math for the Dashboard's three charts.
 *
 * Hand written rather than pulled from a charting library, for the same
 * reason the workflow graph and the trace waterfall are hand drawn: the
 * bundle has a hard gzip budget enforced at build time (decision K-002).
 * Kept pure and DOM-free so it is unit-testable without a browser; the
 * rendering lives in `components/charts.tsx`.
 */

/** Maps a domain value to a pixel position. Degenerate domains map to the range's start. */
export function scaleLinear(domain: [number, number], range: [number, number]): (value: number) => number {
  const [d0, d1] = domain;
  const [r0, r1] = range;
  const span = d1 - d0;

  if (span === 0) {
    return () => r0;
  }

  return (value: number) => r0 + ((value - d0) / span) * (r1 - r0);
}

export interface Point {
  x: number;
  y: number;
}

/** SVG path data for a polyline through the given points. Empty input yields an empty path. */
export function linePath(points: readonly Point[]): string {
  if (points.length === 0) {
    return '';
  }

  return points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${round(point.x)} ${round(point.y)}`).join(' ');
}

export interface BarRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Lays out one bar per value, scaled against the largest value in the set.
 *
 * A value of `0` (or an all-zero series) produces zero-height bars rather
 * than `NaN` — the empty-state message is the caller's job, this function
 * always returns a valid, drawable layout.
 */
export function barLayout(values: readonly number[], width: number, height: number, gapRatio = 0.3): BarRect[] {
  if (values.length === 0) {
    return [];
  }

  const max = Math.max(...values, 0);
  const slot = width / values.length;
  const gap = slot * gapRatio;
  const barWidth = Math.max(slot - gap, 0);

  return values.map((value, index) => {
    const barHeight = max === 0 ? 0 : (value / max) * height;

    return {
      x: index * slot + gap / 2,
      y: height - barHeight,
      width: barWidth,
      height: barHeight,
    };
  });
}

export interface StackedSegment {
  key: string;
  y: number;
  height: number;
}

/**
 * Lays out a vertical stack of segments for one bucket, in `order`.
 *
 * Keys absent from a given record are treated as `0` — a bucket with no
 * failures still produces a (zero-height) failed segment, so the caller
 * never has to special-case a missing key.
 */
export function stackedSegments(
  values: Readonly<Record<string, number>>,
  order: readonly string[],
  height: number,
): StackedSegment[] {
  const total = order.reduce((sum, key) => sum + (values[key] ?? 0), 0);

  if (total === 0) {
    return order.map((key) => ({ key, y: height, height: 0 }));
  }

  let cursor = height;

  return order.map((key) => {
    const segmentHeight = ((values[key] ?? 0) / total) * height;
    const y = cursor - segmentHeight;

    cursor = y;

    return { key, y, height: segmentHeight };
  });
}

/**
 * Picks a legible subset of indices to label, evenly spaced, always
 * including the first and last. Below `maxTicks` every index is kept.
 */
export function tickIndices(count: number, maxTicks: number): number[] {
  if (count <= 0) {
    return [];
  }

  if (count <= maxTicks || maxTicks <= 1) {
    return Array.from({ length: count }, (_, index) => index);
  }

  const step = (count - 1) / (maxTicks - 1);
  const indices = new Set<number>();

  for (let i = 0; i < maxTicks; i++) {
    indices.add(Math.round(i * step));
  }

  return [...indices].sort((a, b) => a - b);
}

function round(value: number): number {
  return Math.round(value * 10) / 10;
}

/** One slice of the token breakdown bar. */
export interface TokenSlice {
  /** Message key naming this slice. */
  key: 'cachedInput' | 'input' | 'reasoning' | 'output';
  /** Token count in this slice. */
  tokens: number;
  /** Share of the whole bar, 0-1. */
  share: number;
}

/** The four counters a token breakdown bar is drawn from. */
export interface TokenTotals {
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  reasoningTokens: number;
}

/** The top of each `RunScoreKind`'s value range, for the trend chart's Y axis. */
export const SCORE_KIND_MAX: Record<string, number> = { Binary: 1, Stars: 5, Numeric: 100 };

/** One (name, kind) group's average, as carried by `RunScoreBucketAggregate.groups`. */
export interface ScoreGroup {
  key: string;
  kind: string;
  average: number | null;
}

/** One bucket of the persistent score summary's trend series. */
export interface ScoreBucket {
  groups: readonly ScoreGroup[];
}

/**
 * Picks the single (name, kind) identity a score trend chart plots across
 * the WHOLE series: `overall` if any bucket carries it, otherwise whichever
 * (name, kind) pair appears in the most buckets.
 *
 * Fixed once for the series so the line never mixes two different score
 * identities across buckets — a per-bucket fallback would let a Binary
 * `overall` average connect straight to a Numeric `accuracy` average with no
 * visual break, and the two ranges plotted on one axis are meaningless
 * together.
 */
export function primaryScoreIdentity(series: readonly ScoreBucket[]): { key: string; kind: string } | undefined {
  const candidates = new Map<string, { key: string; kind: string; bucketCount: number }>();

  for (const bucket of series) {
    for (const group of bucket.groups) {
      if (group.average === null) {
        continue;
      }

      const id = `${group.kind} ${group.key}`;
      const existing = candidates.get(id);

      if (existing) {
        existing.bucketCount += 1;
      } else {
        candidates.set(id, { key: group.key, kind: group.kind, bucketCount: 1 });
      }
    }
  }

  const ranked = [...candidates.values()];
  const winner =
    ranked.find((candidate) => candidate.key === 'overall') ??
    ranked.sort((a, b) => b.bucketCount - a.bucketCount || a.key.localeCompare(b.key))[0];

  return winner === undefined ? undefined : { key: winner.key, kind: winner.kind };
}

/**
 * Splits input/output totals into the four disjoint slices of a breakdown bar.
 *
 * 🚨 `cachedInputTokens` and `reasoningTokens` are counted INSIDE the input and
 * output totals, so the bar is built by SUBTRACTION: the plain "input" slice is
 * the input that was NOT served from cache. Stacking the four raw counters would
 * draw a bar longer than the tokens actually spent.
 *
 * A breakdown larger than the total it belongs to is a provider data fault; the
 * slice is floored at zero rather than drawn as a negative width, and the
 * remaining slices stay truthful.
 *
 * @param totals The four counters from `GET /api/stats`.
 * @returns The slices in draw order, empty ones dropped. Empty when nothing was spent.
 */
export function tokenBreakdown(totals: TokenTotals): TokenSlice[] {
  const cached = Math.max(0, Math.min(totals.cachedInputTokens, totals.inputTokens));
  const reasoning = Math.max(0, Math.min(totals.reasoningTokens, totals.outputTokens));

  const freshInput = Math.max(0, totals.inputTokens - cached);
  const plainOutput = Math.max(0, totals.outputTokens - reasoning);

  const slices: { key: TokenSlice['key']; tokens: number }[] = [
    { key: 'cachedInput', tokens: cached },
    { key: 'input', tokens: freshInput },
    { key: 'reasoning', tokens: reasoning },
    { key: 'output', tokens: plainOutput },
  ];

  const total = slices.reduce((sum, slice) => sum + slice.tokens, 0);

  if (total === 0) {
    return [];
  }

  return slices
    .filter((slice) => slice.tokens > 0)
    .map((slice) => ({ ...slice, share: slice.tokens / total }));
}
