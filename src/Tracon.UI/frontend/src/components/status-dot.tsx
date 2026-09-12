import type { ReactNode } from 'react';
import type { RunStatus } from '@tracon/client';
import { cx } from './ui';

/**
 * The status vocabulary.
 *
 * 🚨 This module is the ONLY place a status may be turned into a colour. Before
 * it existed each screen picked its own green and its own amber, so the same
 * run state read as two different things two clicks apart.
 *
 * The six tones are defined in `styles.css` and mean:
 *
 *   accent   live      — in flight right now
 *   success  cleared   — finished the way it was supposed to
 *   warn     holding   — queued, waiting on a human, degraded
 *   danger   denied    — failed, rejected, unhealthy
 *   info     contact   — informational lifecycle, not an outcome
 *   neutral  standby   — cancelled, idle, structural
 */
export type StatusTone = 'neutral' | 'accent' | 'success' | 'warn' | 'danger' | 'info';

/** Foreground colour for text that carries a status by itself. */
export const STATUS_TEXT: Record<StatusTone, string> = {
  neutral: 'text-muted',
  accent: 'text-accent',
  success: 'text-success',
  warn: 'text-warn',
  danger: 'text-danger',
  info: 'text-info',
};

/** Filled dot / bar colour. */
export const STATUS_FILL: Record<StatusTone, string> = {
  neutral: 'bg-line-strong',
  accent: 'bg-accent',
  success: 'bg-success',
  warn: 'bg-warn',
  danger: 'bg-danger',
  info: 'bg-info',
};

/** Chip surface: a soft field with its own foreground. */
export const STATUS_CHIP: Record<StatusTone, string> = {
  neutral: 'bg-raised text-muted border-line-strong',
  accent: 'bg-accent-soft text-accent border-transparent',
  success: 'bg-success-soft text-success border-transparent',
  warn: 'bg-warn-soft text-warn border-transparent',
  danger: 'bg-danger-soft text-danger border-transparent',
  info: 'bg-info-soft text-info border-transparent',
};

/**
 * The tone a run status reads as.
 *
 * Exhaustive on purpose: `Record<RunStatus, …>` makes a new server status a
 * COMPILE error here rather than a silently grey badge on six screens.
 */
const RUN_STATUS_TONE: Record<RunStatus, StatusTone> = {
  Running: 'accent',
  Completed: 'success',
  Failed: 'danger',
  Canceled: 'neutral',
  AwaitingInput: 'warn',
  AwaitingApproval: 'warn',
  Queued: 'info',
};

export function runStatusTone(status: RunStatus): StatusTone {
  return RUN_STATUS_TONE[status] ?? 'info';
}

/**
 * A status marker.
 *
 * 🚨 Colour alone never carries the meaning: either a `label` rides next to the
 * dot, or the dot is decorative and the status is spelled out beside it. A dot
 * with neither is not accessible and this component refuses to produce one —
 * without a label it marks itself `aria-hidden`, so a screen reader is never
 * told "bullet" and left to guess.
 */
export function StatusDot({
  tone,
  label,
  live = false,
  className,
}: {
  tone: StatusTone;
  /** Announced to assistive technology. Omit ONLY when adjacent text says it. */
  label?: string;
  /** Adds the slow pulse that marks a thing still in flight. */
  live?: boolean;
  className?: string;
}): ReactNode {
  return (
    <span className={cx('relative inline-flex size-2 shrink-0 items-center justify-center', className)}>
      {live && (
        <span
          aria-hidden="true"
          className={cx('absolute inline-flex size-2 animate-ping rounded-full opacity-60', STATUS_FILL[tone])}
        />
      )}
      <span
        aria-hidden={label === undefined ? 'true' : undefined}
        role={label === undefined ? undefined : 'img'}
        aria-label={label}
        className={cx('relative inline-flex size-2 rounded-full', STATUS_FILL[tone])}
      />
    </span>
  );
}
