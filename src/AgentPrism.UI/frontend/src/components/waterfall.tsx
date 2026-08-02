import { useState, type ReactNode } from 'react';
import { Badge, Mono, cx } from './ui';
import type { RunTrace, TraceSpan } from '../lib/types';

/**
 * Trace waterfall.
 *
 * Spans are laid out as bars on a shared time axis so the shape of a run is
 * visible at a glance: which step was slow, what ran inside what, where it
 * failed. Nesting comes from `parentId`, which is derived from the W3C
 * identifiers rather than assigned, so a child can be placed even when its
 * parent finished later.
 *
 * Deliberately hand drawn with CSS rather than a charting library: the bundle
 * has a hard gzip budget enforced at build time, and a bar chart with one
 * dimension does not justify a dependency.
 */
export function Waterfall({ trace }: { trace: RunTrace }): ReactNode {
  const start = new Date(trace.startedAt).getTime();
  const end = trace.endedAt != null ? new Date(trace.endedAt).getTime() : start;

  // A zero-width window would divide by zero and collapse every bar. One
  // millisecond keeps the maths honest for instantaneous traces.
  const total = Math.max(end - start, 1);
  const rows = flatten(trace.spans);

  return (
    <div className="flex flex-col">
      <div className="flex items-center justify-between border-b border-line px-4 py-2 text-[11px] text-subtle">
        <span>
          {rows.length} span{rows.length === 1 ? '' : 's'}
        </span>
        <Mono title="W3C trace id — search for this in your own APM">{trace.traceId}</Mono>
        <span>{formatMs(total)}</span>
      </div>

      <ol className="divide-y divide-line">
        {rows.map((row) => (
          <SpanRow key={row.span.id} row={row} traceStart={start} traceTotal={total} />
        ))}
      </ol>
    </div>
  );
}

interface Row {
  span: TraceSpan;
  depth: number;
}

/**
 * Orders spans as a depth-first tree.
 *
 * Spans whose parent is missing (sampling dropped it, or it belongs to another
 * trace) are treated as roots. Hiding them would silently lose work that
 * actually ran.
 */
function flatten(spans: readonly TraceSpan[]): Row[] {
  const byParent = new Map<string, TraceSpan[]>();
  const known = new Set(spans.map((span) => span.id));

  for (const span of spans) {
    const key = span.parentId != null && known.has(span.parentId) ? span.parentId : '';
    const siblings = byParent.get(key);

    if (siblings === undefined) {
      byParent.set(key, [span]);
    } else {
      siblings.push(span);
    }
  }

  const rows: Row[] = [];

  const walk = (parent: string, depth: number): void => {
    const children = byParent.get(parent) ?? [];

    children.sort((a, b) => new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime());

    for (const span of children) {
      rows.push({ span, depth });
      walk(span.id, depth + 1);
    }
  };

  walk('', 0);

  return rows;
}

function SpanRow({
  row,
  traceStart,
  traceTotal,
}: {
  row: Row;
  traceStart: number;
  traceTotal: number;
}): ReactNode {
  const [open, setOpen] = useState(false);
  const { span, depth } = row;

  const spanStart = new Date(span.startedAt).getTime();
  const spanEnd = span.endedAt != null ? new Date(span.endedAt).getTime() : spanStart;
  const elapsed = Math.max(spanEnd - spanStart, 0);

  const offset = clampPercent(((spanStart - traceStart) / traceTotal) * 100);
  // A floor keeps sub-millisecond spans visible instead of collapsing to nothing.
  const width = Math.max(clampPercent((elapsed / traceTotal) * 100), 0.6);

  const failed = span.status === 'Error';
  const attributes = Object.entries(span.attributes);

  return (
    <li>
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        className="flex w-full items-center gap-3 px-4 py-1.5 text-left hover:bg-raised"
      >
        <span
          className="w-48 shrink-0 truncate text-[12px]"
          style={{ paddingLeft: `${depth * 10}px` }}
          title={span.name}
        >
          {failed && <span className="mr-1 text-danger">!</span>}
          {span.name}
        </span>

        <span className="relative h-3 flex-1 rounded-sm bg-raised">
          <span
            className={cx('absolute inset-y-0 rounded-sm', failed ? 'bg-danger' : 'bg-accent')}
            style={{ left: `${offset}%`, width: `${width}%` }}
          />
        </span>

        <Mono className="w-16 shrink-0 text-right text-[11px] text-muted">{formatMs(elapsed)}</Mono>
      </button>

      {open && (
        <div className="border-t border-line bg-raised px-4 py-2">
          <div className="mb-2 flex flex-wrap items-center gap-1.5">
            <Badge>{span.kind}</Badge>
            <Badge tone={failed ? 'danger' : 'accent'}>{span.status}</Badge>
            <Mono className="text-[11px] text-subtle" title="W3C span id">
              {span.spanId}
            </Mono>
          </div>

          {attributes.length === 0 ? (
            <p className="text-[12px] text-subtle">This span carries no attributes.</p>
          ) : (
            <dl className="grid gap-x-4 gap-y-1 text-[12px] sm:grid-cols-[auto_1fr]">
              {attributes.map(([key, value]) => (
                <div key={key} className="contents">
                  <dt className="font-mono text-subtle">{key}</dt>
                  <dd className="break-all text-muted">{value}</dd>
                </div>
              ))}
            </dl>
          )}
        </div>
      )}
    </li>
  );
}

function clampPercent(value: number): number {
  return Number.isFinite(value) ? Math.min(Math.max(value, 0), 100) : 0;
}

/** Formats a millisecond span the way an operator reads it. */
export function formatMs(value: number): string {
  if (value < 1) {
    return '<1ms';
  }

  if (value < 1_000) {
    return `${Math.round(value)}ms`;
  }

  return `${(value / 1_000).toFixed(value < 10_000 ? 2 : 1)}s`;
}
