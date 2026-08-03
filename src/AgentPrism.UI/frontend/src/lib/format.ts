/** Formatting helpers. Pure functions, covered by unit tests. */

const UNITS: [limit: number, divisor: number, suffix: string][] = [
  [60_000, 1_000, 's'],
  [3_600_000, 60_000, 'm'],
  [86_400_000, 3_600_000, 'h'],
];

/** Compact relative time: `12s ago`, `4m ago`, `3d ago`. */
export function relativeTime(value: string | null | undefined, now: number = Date.now()): string {
  if (!value) {
    return '—';
  }

  const timestamp = Date.parse(value);

  if (Number.isNaN(timestamp)) {
    return '—';
  }

  const elapsed = now - timestamp;

  if (elapsed < 0) {
    return 'just now';
  }

  if (elapsed < 1_000) {
    return 'just now';
  }

  for (const [limit, divisor, suffix] of UNITS) {
    if (elapsed < limit) {
      return `${Math.floor(elapsed / divisor)}${suffix} ago`;
    }
  }

  return `${Math.floor(elapsed / 86_400_000)}d ago`;
}

/** Absolute timestamp for tooltips and detail panels. */
export function absoluteTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const timestamp = new Date(value);

  return Number.isNaN(timestamp.getTime()) ? '—' : timestamp.toLocaleString();
}

/** Elapsed time between two instants, as `1.24s` or `340ms`. */
export function duration(from: string | null | undefined, to: string | null | undefined): string {
  if (!from || !to) {
    return '—';
  }

  const start = Date.parse(from);
  const end = Date.parse(to);

  if (Number.isNaN(start) || Number.isNaN(end)) {
    return '—';
  }

  const elapsed = end - start;

  if (elapsed < 1_000) {
    return `${elapsed}ms`;
  }

  if (elapsed < 60_000) {
    return `${(elapsed / 1_000).toFixed(2)}s`;
  }

  return `${Math.floor(elapsed / 60_000)}m ${Math.round((elapsed % 60_000) / 1_000)}s`;
}

/** Parses a .NET `TimeSpan` wire string (`hh:mm:ss[.fffffff]`) into milliseconds. */
export function timeSpanMs(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }

  const match = /^(\d+):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value);

  if (!match) {
    return null;
  }

  const [, hours, minutes, seconds, fraction] = match;

  return (
    Number(hours) * 3_600_000 +
    Number(minutes) * 60_000 +
    Number(seconds) * 1_000 +
    (fraction ? Number(fraction.padEnd(3, '0').slice(0, 3)) : 0)
  );
}

/** Health-check latency as `120ms` or `1.24s`, or an em dash when unknown. */
export function latencyText(value: string | null | undefined): string {
  const ms = timeSpanMs(value);

  if (ms === null) {
    return '—';
  }

  return ms < 1_000 ? `${ms}ms` : `${(ms / 1_000).toFixed(2)}s`;
}

/** Thousands-separated integer. */
export function count(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : value.toLocaleString();
}

/** Percentage with one decimal, or an em dash when the ratio is unknown. */
export function percent(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : `${(value * 100).toFixed(1)}%`;
}

/**
 * Money amount with a currency suffix, or an em dash when the price is
 * undefined (never shown as `0` — an undefined price is not a free model).
 * No currency conversion is performed; the label is whatever the operator
 * configured under `AgentPrism:Pricing:Currency`.
 */
export function money(value: number | null | undefined, currency: string | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  const amount = value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 });

  return currency ? `${amount} ${currency}` : amount;
}

/** Shortens an identifier for dense tables: `019fc02e…5f21`. */
export function shortId(value: string | null | undefined, head = 8, tail = 4): string {
  if (!value) {
    return '—';
  }

  return value.length <= head + tail + 1 ? value : `${value.slice(0, head)}…${value.slice(-tail)}`;
}

/** Pretty-prints JSON, falling back to the original text when it is not JSON. */
export function prettyJson(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    // Tool arguments are formatted by hand on the server to stay AOT friendly
    // and are not guaranteed to be valid JSON. Showing the raw text is correct.
    return value;
  }
}
