import type { ReactNode } from 'react';
import { diffLines, diffSets } from '../lib/diff';
import { useT } from '../lib/i18n';
import { Badge, cx } from './ui';

/**
 * Line-by-line diff of two texts. Shared by version comparison (`Instructions`)
 * and the audit trail's before/after view — the server never computes a diff,
 * it only returns the two raw values.
 */
export function DiffView({
  left,
  right,
  className,
}: {
  left: string;
  right: string;
  className?: string;
}): ReactNode {
  const t = useT();
  const { lines, truncated } = diffLines(left, right);

  if (lines.length === 0) {
    return <p className="text-sm text-muted">{t('diff.bothEmpty')}</p>;
  }

  return (
    <div className={cx('overflow-x-auto rounded-md border border-line', className)}>
      {truncated && (
        <p className="border-b border-line bg-warn-soft px-3 py-1.5 text-xs text-warn">
          {t('diff.truncated')}
        </p>
      )}
      <pre className="font-mono text-sm leading-5">
        {lines.map((line, index) => (
          <div
            key={index}
            className={cx(
              'flex gap-3 px-3',
              line.kind === 'added' && 'bg-success-soft',
              line.kind === 'removed' && 'bg-danger-soft',
            )}
          >
            <span className="w-10 shrink-0 text-right text-subtle">{line.leftNo ?? ''}</span>
            <span className="w-10 shrink-0 text-right text-subtle">{line.rightNo ?? ''}</span>
            <span
              className={cx(
                'w-4 shrink-0',
                line.kind === 'added' && 'text-success',
                line.kind === 'removed' && 'text-danger',
              )}
            >
              {line.kind === 'added' ? '+' : line.kind === 'removed' ? '-' : ' '}
            </span>
            <span className="whitespace-pre-wrap break-all">{line.text}</span>
          </div>
        ))}
      </pre>
    </div>
  );
}

export interface FieldDiffRow {
  /** Column shown to the user. */
  label: string;
  /** Formats a raw field value for display; defaults to `String(value)`, `'—'` for null/undefined. */
  format?: (value: unknown) => string;
}

/**
 * Field-by-field comparison table for structured settings (`Model`, `Harness`,
 * `Compaction`, `Memory`). Unlike `DiffView`, there is no line alignment here —
 * each field is one row, and rows where the two sides differ are highlighted.
 */
export function FieldDiffTable<T extends object>({
  left,
  right,
  fields,
}: {
  left?: T | null;
  right?: T | null;
  fields: Record<string, FieldDiffRow>;
}): ReactNode {
  const t = useT();
  const leftRecord = left as unknown as Record<string, unknown> | undefined;
  const rightRecord = right as unknown as Record<string, unknown> | undefined;

  const format = (row: FieldDiffRow, value: unknown): string => {
    if (value === null || value === undefined) {
      return '—';
    }

    return row.format ? row.format(value) : String(value);
  };

  return (
    <table className="w-full border-collapse text-base">
      <thead>
        <tr>
          <th className="border-b border-line px-3 py-1.5 text-left text-xs font-semibold tracking-wide text-subtle uppercase">
            {t('diff.field')}
          </th>
          <th className="border-b border-line px-3 py-1.5 text-left text-xs font-semibold tracking-wide text-subtle uppercase">
            {t('diff.left')}
          </th>
          <th className="border-b border-line px-3 py-1.5 text-left text-xs font-semibold tracking-wide text-subtle uppercase">
            {t('diff.right')}
          </th>
        </tr>
      </thead>
      <tbody>
        {Object.entries(fields).map(([key, row]) => {
          const leftValue = format(row, leftRecord?.[key]);
          const rightValue = format(row, rightRecord?.[key]);
          const changed = leftValue !== rightValue;

          return (
            <tr key={key} className={changed ? 'bg-warn-soft' : undefined}>
              <td className="border-b border-line px-3 py-1.5 align-top text-muted">{row.label}</td>
              <td className="border-b border-line px-3 py-1.5 align-top font-mono text-sm">{leftValue}</td>
              <td className="border-b border-line px-3 py-1.5 align-top font-mono text-sm">{rightValue}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

/**
 * Set difference view for name lists (`toolNames`, `skillNames`,
 * `callableAgentNames`): added names in green, removed in red, unchanged neutral.
 */
export function SetDiff({
  left,
  right,
  label,
}: {
  left: readonly string[];
  right: readonly string[];
  label: string;
}): ReactNode {
  const t = useT();
  const { added, removed, unchanged } = diffSets(left, right);

  if (added.length === 0 && removed.length === 0 && unchanged.length === 0) {
    return null;
  }

  return (
    <div>
      <p className="mb-1.5 text-xs font-semibold tracking-wide text-subtle uppercase">{label}</p>
      <div className="flex flex-wrap gap-1.5">
        {unchanged.map((name) => (
          <Badge key={`same-${name}`} tone="neutral">
            {name}
          </Badge>
        ))}
        {removed.map((name) => (
          <Badge key={`removed-${name}`} tone="danger" description={t('diff.removed')}>
            − {name}
          </Badge>
        ))}
        {added.map((name) => (
          <Badge key={`added-${name}`} tone="success" description={t('diff.added')}>
            + {name}
          </Badge>
        ))}
      </div>
    </div>
  );
}
