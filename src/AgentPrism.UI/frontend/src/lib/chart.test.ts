import { describe, expect, it } from 'vitest';
import { barLayout, linePath, scaleLinear, stackedSegments, tickIndices } from './chart';

describe('scaleLinear', () => {
  it('maps domain edges to range edges', () => {
    const scale = scaleLinear([0, 100], [10, 210]);

    expect(scale(0)).toBe(10);
    expect(scale(100)).toBe(210);
    expect(scale(50)).toBe(110);
  });

  it('maps a degenerate domain to the range start instead of dividing by zero', () => {
    const scale = scaleLinear([5, 5], [0, 100]);

    expect(scale(5)).toBe(0);
    expect(Number.isNaN(scale(5))).toBe(false);
  });
});

describe('linePath', () => {
  it('builds a moveto followed by linetos', () => {
    expect(linePath([{ x: 0, y: 0 }, { x: 10, y: 5 }, { x: 20, y: 0 }])).toBe('M 0 0 L 10 5 L 20 0');
  });

  it('returns an empty path for no points', () => {
    expect(linePath([])).toBe('');
  });
});

describe('barLayout', () => {
  it('scales bars against the largest value', () => {
    const bars = barLayout([10, 5, 0], 90, 100, 0);

    expect(bars).toHaveLength(3);
    expect(bars[0]?.height).toBe(100);
    expect(bars[1]?.height).toBe(50);
    expect(bars[2]?.height).toBe(0);
  });

  it('produces zero-height bars instead of NaN when every value is zero', () => {
    const bars = barLayout([0, 0], 90, 100);

    expect(bars.every((bar) => bar.height === 0 && !Number.isNaN(bar.height))).toBe(true);
  });

  it('returns an empty layout for no data', () => {
    expect(barLayout([], 90, 100)).toEqual([]);
  });
});

describe('stackedSegments', () => {
  it('stacks segments proportionally in the given order', () => {
    const segments = stackedSegments({ ok: 3, failed: 1 }, ['ok', 'failed'], 100);

    expect(segments[0]?.key).toBe('ok');
    expect(segments[0]?.height).toBe(75);
    expect(segments[1]?.key).toBe('failed');
    expect(segments[1]?.height).toBe(25);
  });

  it('treats a missing key as zero rather than throwing', () => {
    const segments = stackedSegments({ ok: 5 }, ['ok', 'failed'], 100);

    expect(segments[1]?.height).toBe(0);
  });

  it('returns zero-height segments instead of NaN when the bucket is empty', () => {
    const segments = stackedSegments({}, ['ok', 'failed'], 100);

    expect(segments.every((segment) => segment.height === 0)).toBe(true);
  });
});

describe('tickIndices', () => {
  it('keeps every index when under the limit', () => {
    expect(tickIndices(3, 5)).toEqual([0, 1, 2]);
  });

  it('thins labels once the count exceeds the limit, keeping the first and last', () => {
    const indices = tickIndices(100, 5);

    expect(indices[0]).toBe(0);
    expect(indices[indices.length - 1]).toBe(99);
    expect(indices.length).toBeLessThanOrEqual(5);
  });

  it('returns nothing for an empty series', () => {
    expect(tickIndices(0, 5)).toEqual([]);
  });
});
