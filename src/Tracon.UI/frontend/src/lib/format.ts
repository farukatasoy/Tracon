/**
 * Formatting helpers. Pure functions, covered by unit tests.
 *
 * Anything a person reads goes through `Intl`, which the browser already has:
 * Turkish writes `1.234,5` where English writes `1,234.5`, and a number
 * formatted the wrong way is misread, not just mis-styled.
 *
 * SI unit symbols (`ms`, `s`, `m`) are NOT translated. They are symbols, and
 * `duration` stays identical in every language on purpose.
 */

import { formatDateTime, formatNumber, formatRelative } from './i18n';

const UNITS: [limit: number, divisor: number, unit: Intl.RelativeTimeFormatUnit][] = [
  [60_000, 1_000, 'second'],
  [3_600_000, 60_000, 'minute'],
  [86_400_000, 3_600_000, 'hour'],
];

/** Compact relative time: `12 sec. ago`, `4 min. ago`, `3 days ago`. */
export function relativeTime(value: string | null | undefined, now: number = Date.now()): string {
  if (!value) {
    return '—';
  }

  const timestamp = Date.parse(value);

  if (Number.isNaN(timestamp)) {
    return '—';
  }

  const elapsed = now - timestamp;

  // A clock skew between server and browser must not read as the future.
  if (elapsed < 1_000) {
    return formatRelative(0, 'second');
  }

  for (const [limit, divisor, unit] of UNITS) {
    if (elapsed < limit) {
      return formatRelative(-Math.floor(elapsed / divisor), unit);
    }
  }

  return formatRelative(-Math.floor(elapsed / 86_400_000), 'day');
}

/** Absolute timestamp for tooltips and detail panels. */
export function absoluteTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  const timestamp = new Date(value);

  return Number.isNaN(timestamp.getTime()) ? '—' : formatDateTime(timestamp);
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

/** Thousands-separated integer, grouped the way the active language groups. */
export function count(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : formatNumber(value);
}

/** Percentage with one decimal, or an em dash when the ratio is unknown. */
export function percent(value: number | null | undefined): string {
  return value === null || value === undefined
    ? '—'
    : formatNumber(value, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

/**
 * Money amount with a currency suffix, or an em dash when the price is
 * undefined (never shown as `0` — an undefined price is not a free model).
 * No currency conversion is performed; the label is whatever the operator
 * configured under `Tracon:Pricing:Currency`.
 */
export function money(value: number | null | undefined, currency: string | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  // A single turn on a cheap enough model can cost less than 4 decimals can
  // represent (e.g. 0.00003945): rounded to "0.00" it reads as free, which is
  // exactly the "0 is not the same as unknown" mixup this function otherwise
  // guards against for a `null` price. More decimals only for the amounts
  // that need them — an ordinary cost still prints as it always did.
  const maximumFractionDigits = value > 0 && value < 0.01 ? 6 : 4;
  const amount = formatNumber(value, { minimumFractionDigits: 2, maximumFractionDigits });

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
