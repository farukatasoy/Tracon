import { useMemo, type ReactNode } from 'react';
import { barLayout, linePath, scaleLinear, stackedSegments, tickIndices } from '../lib/chart';
import { count, money } from '../lib/format';
import type { RunModelStatistics, TimeSeriesPoint } from '../lib/types';

const EMPTY_MESSAGE = 'Bu aralıkta çalıştırma yok';

/**
 * A faint wash of a theme colour. Same trick as the workflow graph: mixed at
 * paint time rather than added as new `*-soft` tokens, so both themes stay
 * in sync automatically.
 */
function tint(token: string): string {
  return `color-mix(in srgb, var(${token}) 16%, transparent)`;
}

function EmptyChart({ height }: { height: number }): ReactNode {
  return (
    <div
      style={{ height }}
      className="flex items-center justify-center text-[12px] text-subtle"
    >
      {EMPTY_MESSAGE}
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
  const padding = { top: 10, right: 12, bottom: 20, left: 12 };
  const plotWidth = width - padding.left - padding.right;
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
      role="img"
      aria-label="Zaman serisi: çalıştırma ve hata sayısı"
      data-testid="timeseries-chart"
      viewBox={`0 0 ${width} ${height}`}
      width="100%"
      height={height}
      preserveAspectRatio="none"
      className="max-w-full"
    >
      <g transform={`translate(${padding.left}, ${padding.top})`}>
        <path d={layout.runsPath} fill="none" stroke="var(--ap-violet)" strokeWidth={1.75} />
        <path
          d={layout.failedPath}
          fill="none"
          stroke="var(--ap-rose)"
          strokeWidth={1.5}
          strokeDasharray="4 3"
        />

        {layout.ticks.map((tick) => (
          <text
            key={tick.index}
            x={tick.x}
            y={plotHeight + 16}
            textAnchor="middle"
            fill="var(--ap-subtle)"
            className="text-[10px]"
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
        <div key={model.modelId} className="flex items-center gap-3 text-[12px]">
          <span className="w-32 shrink-0 truncate text-muted" title={model.modelId}>
            {model.modelId}
          </span>
          <div className="h-4 flex-1 rounded bg-raised">
            <div
              style={{ width: `${(model.totalRuns / max) * 100}%`, background: tint('--ap-violet') }}
              className="h-full rounded"
            />
          </div>
          <span className="w-16 shrink-0 text-right text-subtle">{count(model.totalRuns)} run</span>
          <span className="w-16 shrink-0 text-right text-subtle">{count(model.totalTokens)} tok</span>
          <span className="w-28 shrink-0 text-right font-medium">
            {model.totalCost === undefined ? '—' : money(model.totalCost, currency)}
          </span>
        </div>
      ))}
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
  if (points.length === 0) {
    return <EmptyChart height={height} />;
  }

  const bars = barLayoutFor(points, width, height);

  return (
    <svg
      role="img"
      aria-label="Zaman serisi: durum dağılımı"
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
              fill={segment.key === 'failed' ? 'var(--ap-rose)' : tint('--ap-emerald')}
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
