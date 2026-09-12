import { useId, type ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { SearchIcon } from './icons';
import { Button, cx } from './ui';

/**
 * The strip above a list: search, filters, count, actions.
 *
 * Every list screen used to lay this out for itself, so the same three controls
 * sat in a different order and at a different height on each one. One component
 * now owns the order — search, filters, then the reset — and the row wraps
 * instead of overflowing at narrow widths.
 */
export function Toolbar({
  search,
  children,
  actions,
  summary,
  onReset,
}: {
  search?: { value: string; onChange: (value: string) => void; label: string; placeholder?: string };
  /** Selects and toggles. Each one labels itself; see `ToolbarField`. */
  children?: ReactNode;
  /** Primary actions, pushed to the right. */
  actions?: ReactNode;
  /** What the filters currently select — a count, a range. */
  summary?: ReactNode;
  /** Shown only while something is actually filtered. */
  onReset?: (() => void) | undefined;
}): ReactNode {
  const t = useT();

  return (
    <div
      role="toolbar"
      aria-label={t('toolbar.label')}
      aria-orientation="horizontal"
      className="mb-3 flex flex-wrap items-end gap-2"
    >
      {search !== undefined && (
        <label className="relative flex min-w-40 flex-1 items-center sm:max-w-64">
          <span className="sr-only">{search.label}</span>
          <SearchIcon className="pointer-events-none absolute left-2 size-3.5 text-subtle" />
          <input
            type="search"
            data-search
            value={search.value}
            placeholder={search.placeholder ?? search.label}
            onChange={(event) => search.onChange(event.target.value)}
            className={cx(
              'h-8 w-full rounded border border-line-strong bg-panel pr-2 pl-7 text-base text-fg',
              'placeholder:text-subtle focus:border-accent focus:outline-none',
            )}
          />
        </label>
      )}

      {children}

      {summary !== undefined && <span className="text-xs text-subtle">{summary}</span>}

      {onReset !== undefined && (
        <Button tone="ghost" onClick={onReset} testId="toolbar-reset">
          {t('toolbar.reset')}
        </Button>
      )}

      {actions !== undefined && <div className="ml-auto flex items-center gap-2">{actions}</div>}
    </div>
  );
}

/**
 * A labelled toolbar control.
 *
 * 🚨 A bare `<select>` in a filter strip has no accessible name — its options
 * are its only text, and "All agents" is not what the control IS. The label is
 * visible here rather than hidden: an operator scanning a strip of four selects
 * needs to know which one narrows what.
 */
export function ToolbarField({
  label,
  children,
}: {
  label: string;
  /** Receives the generated id, so the control binds to the label. */
  children: (id: string) => ReactNode;
}): ReactNode {
  const id = useId();

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-2xs font-medium tracking-wide text-subtle uppercase">
        {label}
      </label>
      {children(id)}
    </div>
  );
}
