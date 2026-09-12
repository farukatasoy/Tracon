import { useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  barLayout,
  linePath,
  primaryScoreIdentity,
  scaleLinear,
  SCORE_KIND_MAX,
  stackedSegments,
  tickIndices,
  tokenBreakdown,
} from '../lib/chart';
import { count, money } from '../lib/format';
import { useT } from '../lib/i18n';
import type {
  RunModelStatistics,
  RunScoreAggregate,
  RunScoreBucketAggregate,
  RunStatistics,
  TimeSeriesPoint,
} from '../lib/server-types';

/**
 * The panel these charts sit in is wider than their `viewBox`, so `width:
 * 100%` plus `preserveAspectRatio="none"` used to stretch every coordinate —
 * including tick-label glyphs — horizontally by whatever ratio the panel
 * happened to be. Measuring the SVG's actual rendered width and feeding that
 * back into the viewBox makes the two match, so there is nothing left to
 * stretch.
 */
function useMeasuredWidth(fallback: number) {
  const ref = useRef<SVGSVGElement>(null);
  const [width, setWidth] = useState(fallback);

  useLayoutEffect(() => {
    const node = ref.current;

    if (node === null || typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      if (entry !== undefined && entry.contentRect.width > 0) {
        setWidth(entry.contentRect.width);
      }
    });

    observer.observe(node);

    return () => observer.disconnect();
  }, []);

  return [ref, width] as const;
}

function EmptyChart({ height }: { height: number }): ReactNode {
  const t = useT();

  return (
    <div
      style={{ height }}
      className="flex items-center justify-center text-sm text-subtle"
    >
      {t('charts.noRuns')}
    </div>
  );
}

/**
 * Runs-per-bucket line, with a dashed failed-runs line drawn on the same
 * scale. Colour is never the only signal: the error series is dashed so it
 * still reads without colour vision.
 */
export function TimeSeriesChart({
  points,
  height = 160,
  width = 640,
}: {
  points: readonly TimeSeriesPoint[];
  height?: number;
  width?: number;
}): ReactNode {
  const t = useT();
  const [svgRef, renderedWidth] = useMeasuredWidth(width);
  // 🚨 The horizontal padding holds the FIRST and LAST tick labels, which are
  // anchored to the plot's edges. At 12 px they were clipped by the panel.
  const padding = { top: 10, right: 16, bottom: 20, left: 16 };
  const plotWidth = renderedWidth - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;

  const layout = useMemo(() => {
    if (points.length === 0) {
      return null;
    }

    const max = Math.max(1, ...points.map((point) => point.runs));
    const xScale = scaleLinear([0, Math.max(points.length - 1, 1)], [0, plotWidth]);
    const yScale = scaleLinear([0, max], [plotHeight, 0]);

    const runsPath = linePath(
      points.map((point, index) => ({ x: xScale(index), y: yScale(point.runs) })),
    );
    const failedPath = linePath(
      points.map((point, index) => ({ x: xScale(index), y: yScale(point.failedRuns) })),
    );

    const ticks = tickIndices(points.length, 6).flatMap((index) => {
      const point = points[index];

      return point === undefined ? [] : [{ index, x: xScale(index), label: bucketLabel(point.bucket) }];
    });

    return { runsPath, failedPath, ticks };
  }, [points, plotWidth, plotHeight]);

  if (layout === null) {
    return <EmptyChart height={height} />;
  }

  return (
    <svg
      ref={svgRef}
      role="img"
      aria-label={t('charts.timeSeriesLabel')}
      data-testid="timeseries-chart"
      viewBox={`0 0 ${renderedWidth} ${height}`}
      width="100%"
      height={height}
      preserveAspectRatio="none"
      className="max-w-full"
    >
      <g transform={`translate(${padding.left}, ${padding.top})`}>
        <path d={layout.runsPath} fill="none" stroke="var(--tracon-series-1)" strokeWidth={1.75} />
        <path
          d={layout.failedPath}
          fill="none"
          stroke="var(--tracon-danger)"
          strokeWidth={1.5}
          strokeDasharray="4 3"
        />

        {layout.ticks.map((tick, index) => (
          <text
            key={tick.index}
            x={tick.x}
            y={plotHeight + 16}
            textAnchor={index === 0 ? 'start' : index === layout.ticks.length - 1 ? 'end' : 'middle'}
            fill="var(--tracon-subtle)"
            className="text-2xs"
          >
            {tick.label}
          </text>
        ))}
      </g>
    </svg>
  );
}

function bucketLabel(bucket: string): string {
  const date = new Date(bucket);

  return Number.isNaN(date.getTime())
    ? bucket
    : date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric' });
}

/**
 * The persistent score trend (unlike the live online-evaluation panel, this
 * reads `GET /api/evaluation/scores/summary` and survives a server restart).
 * One line: a single (name, kind) identity's average per bucket — `overall`
 * when present anywhere in the series, otherwise the series' most common
 * score. A bucket that never scored that identity leaves a gap rather than
 * plotting a different metric in its place.
 */
export function ScoreTrendChart({
  series,
  height = 120,
  width = 640,
}: {
  series: readonly RunScoreBucketAggregate[];
  height?: number;
  width?: number;
}): ReactNode {
  const t = useT();
  const [svgRef, renderedWidth] = useMeasuredWidth(width);
  // 🚨 The horizontal padding holds the FIRST and LAST tick labels, which are
  // anchored to the plot's edges. At 12 px they were clipped by the panel.
  const padding = { top: 10, right: 16, bottom: 20, left: 16 };
  const plotWidth = renderedWidth - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;

  const layout = useMemo(() => {
    const identity = primaryScoreIdentity(series);

    if (identity === undefined) {
      return null;
    }

    const points = series
      .map((bucket) => ({
        bucketStart: bucket.bucketStart,
        group: bucket.groups.find(
          (group) => group.key === identity.key && group.kind === identity.kind && group.average !== null,
        ),
      }))
      .filter((point): point is { bucketStart: string; group: RunScoreAggregate } => point.group !== undefined);

    if (points.length === 0) {
      return null;
    }

    const domainMax = SCORE_KIND_MAX[identity.kind] ?? 100;
    const xScale = scaleLinear([0, Math.max(points.length - 1, 1)], [0, plotWidth]);
    const yScale = scaleLinear([0, domainMax], [plotHeight, 0]);

    const dots = points.map((point, index) => ({ x: xScale(index), y: yScale(point.group.average ?? 0) }));

    // A single point draws no visible line (linePath needs two); a dot per
    // point keeps a one-bucket series from rendering as an empty box.
    const path = linePath(dots);

    const ticks = tickIndices(points.length, 6).flatMap((index) => {
      const point = points[index];

      return point === undefined ? [] : [{ index, x: xScale(index), label: bucketLabel(point.bucketStart) }];
    });

    return { path, dots, ticks };
  }, [series, plotWidth, plotHeight]);

  if (layout === null) {
    return <EmptyChart height={height} />;
  }

  return (
    <svg
      ref={svgRef}
      role="img"
      aria-label={t('charts.scoreTrendLabel')}
      data-testid="score-trend-chart"
      viewBox={`0 0 ${renderedWidth} ${height}`}
      width="100%"
      height={height}
      preserveAspectRatio="none"
      className="max-w-full"
    >
      <g transform={`translate(${padding.left}, ${padding.top})`}>
        <path d={layout.path} fill="none" stroke="var(--tracon-series-1)" strokeWidth={1.75} />

        {layout.dots.map((dot, index) => (
          <circle key={index} cx={dot.x} cy={dot.y} r={2.5} fill="var(--tracon-series-1)" />
        ))}

        {layout.ticks.map((tick, index) => (
          <text
            key={tick.index}
            x={tick.x}
            y={plotHeight + 16}
            textAnchor={index === 0 ? 'start' : index === layout.ticks.length - 1 ? 'end' : 'middle'}
            fill="var(--tracon-subtle)"
            className="text-2xs"
          >
            {tick.label}
          </text>
        ))}
      </g>
    </svg>
  );
}

/** Runs, tokens and cost per model, as horizontal bars ordered by run count. */
export function ModelBreakdownChart({
  models,
  currency,
}: {
  models: readonly RunModelStatistics[];
  currency?: string | null;
}): ReactNode {
  if (models.length === 0) {
    return <EmptyChart height={80} />;
  }

  const max = Math.max(1, ...models.map((model) => model.totalRuns));

  return (
    <div data-testid="model-breakdown-chart" className="flex flex-col gap-2 p-4">
      {models.map((model) => (
        // 🚨 Four fixed-width columns plus a flexible bar could not fit a
        // phone: their minimum widths added up past the viewport and pushed the
        // whole page sideways. The bar now takes its own line below the
        // figures, so the row wraps instead of forcing the page wider.
        <div key={model.modelId} className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
          <span className="min-w-24 flex-1 truncate text-muted sm:w-32 sm:flex-none" title={model.modelId}>
            {model.modelId}
          </span>
          <span className="w-14 shrink-0 text-right font-mono text-subtle">{count(model.totalRuns)} run</span>
          <span className="w-16 shrink-0 text-right font-mono text-subtle">{count(model.totalTokens)} tok</span>
          <span className="w-20 shrink-0 text-right font-mono font-medium">
            {model.totalCost === undefined ? '—' : money(model.totalCost, currency)}
          </span>
          <div className="h-1.5 w-full rounded-full bg-raised">
            <div
              style={{ width: `${(model.totalRuns / max) * 100}%`, background: 'var(--tracon-series-1)' }}
              className="h-full rounded-full"
            />
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * Where the tokens actually went: cache hits, fresh input, reasoning, plain output.
 *
 * 🚨 The four slices are DISJOINT, produced by subtraction in
 * {@link tokenBreakdown}. `cachedInputTokens` and `reasoningTokens` are counted
 * INSIDE the input and output totals, so stacking the raw counters would draw a
 * bar longer than the tokens that were spent.
 *
 * Colour is never the only signal: every slice is also named in the legend
 * underneath, with its own token count.
 */
export function TokenBreakdownChart({ stats }: { stats: RunStatistics }): ReactNode {
  const t = useT();

  const slices = useMemo(
    () =>
      tokenBreakdown({
        inputTokens: stats.inputTokens,
        outputTokens: stats.outputTokens,
        cachedInputTokens: stats.cachedInputTokens,
        reasoningTokens: stats.reasoningTokens,
      }),
    [stats],
  );

  if (slices.length === 0) {
    return <EmptyChart height={80} />;
  }

  // Solid tokens, not tints: four adjacent slices have to stay apart from each
  // other. Every one of these is defined in BOTH themes in styles.css.
  const colours: Record<string, string> = {
    cachedInput: 'var(--tracon-series-6)',
    input: 'var(--tracon-series-1)',
    reasoning: 'var(--tracon-series-5)',
    output: 'var(--tracon-series-4)',
  };

  const labels: Record<string, string> = {
    cachedInput: t('dashboard.tokens.cachedInput'),
    input: t('dashboard.tokens.input'),
    reasoning: t('dashboard.tokens.reasoning'),
    output: t('dashboard.tokens.output'),
  };

  return (
    <div data-testid="token-breakdown-chart" className="flex flex-col gap-3 p-4">
      <div
        role="img"
        aria-label={t('dashboard.tokens.label')}
        className="flex h-4 overflow-hidden rounded bg-raised"
      >
        {slices.map((slice) => (
          <div
            key={slice.key}
            style={{ width: `${slice.share * 100}%`, background: colours[slice.key] }}
            title={`${labels[slice.key]}: ${count(slice.tokens)}`}
            className="h-full"
          />
        ))}
      </div>

      <dl className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
        {slices.map((slice) => (
          <div key={slice.key} className="flex items-center gap-1.5">
            <span
              aria-hidden="true"
              style={{ background: colours[slice.key] }}
              className="size-2.5 shrink-0 rounded-sm"
            />
            <dt className="text-muted">{labels[slice.key]}</dt>
            <dd className="font-medium">{count(slice.tokens)}</dd>
          </div>
        ))}
      </dl>

      {/*
        The one thing a reader cannot infer from the bar: these slices are a
        RE-CUT of the input/output totals, not extra tokens beside them.
      */}
      <p className="text-xs text-subtle">{t('dashboard.tokens.hint')}</p>
    </div>
  );
}

/** Completed vs. failed runs per bucket, as a stacked bar. */
export function StatusDistributionChart({
  points,
  height = 120,
  width = 640,
}: {
  points: readonly TimeSeriesPoint[];
  height?: number;
  width?: number;
}): ReactNode {
  const t = useT();

  if (points.length === 0) {
    return <EmptyChart height={height} />;
  }

  const bars = barLayoutFor(points, width, height);

  return (
    <svg
      role="img"
      aria-label={t('charts.statusLabel')}
      data-testid="status-distribution-chart"
      viewBox={`0 0 ${width} ${height}`}
      width="100%"
      height={height}
      preserveAspectRatio="none"
      className="max-w-full"
    >
      {bars.map(({ x, width: barWidth, segments }, index) => (
        <g key={index}>
          {segments.map((segment) => (
            <rect
              key={segment.key}
              x={x}
              y={segment.y}
              width={barWidth}
              height={segment.height}
              fill={segment.key === 'failed' ? 'var(--tracon-danger)' : 'var(--tracon-series-1)'}
            />
          ))}
        </g>
      ))}
    </svg>
  );
}

function barLayoutFor(
  points: readonly TimeSeriesPoint[],
  width: number,
  height: number,
): { x: number; width: number; segments: { key: string; y: number; height: number }[] }[] {
  const slots = barLayout(
    points.map(() => 1),
    width,
    height,
    0.25,
  );

  return points.map((point, index) => {
    // `slots` is built from `points.map(() => 1)`, so the two arrays always
    // have the same length; the fallback only satisfies noUncheckedIndexedAccess.
    const slot = slots[index] ?? { x: 0, y: 0, width: 0, height: 0 };

    return {
      x: slot.x,
      width: slot.width,
      segments: stackedSegments(
        { completed: Math.max(point.runs - point.failedRuns, 0), failed: point.failedRuns },
        ['completed', 'failed'],
        height,
      ),
    };
  });
}
