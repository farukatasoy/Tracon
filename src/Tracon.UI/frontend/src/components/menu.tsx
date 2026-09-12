import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { ChevronIcon } from './icons';
import { STATUS_TEXT, type StatusTone } from './status-dot';
import { cx } from './ui';

/**
 * A button that opens a list of actions.
 *
 * 🚨 Written by hand for the same reason as `Dialog`: no headless dependency.
 * The parts that get forgotten and are therefore tested here:
 *
 *   - the trigger says `aria-haspopup` / `aria-expanded`;
 *   - the open list owns the arrow keys, Home and End;
 *   - Esc closes it and gives focus BACK to the trigger;
 *   - Tab closes it instead of walking into a floating list;
 *   - a click anywhere else closes it.
 *
 * Focus stays on the list element and the highlighted row is named by
 * `aria-activedescendant`. Moving real DOM focus row by row would fight the
 * shell's global key bindings, which ask what element the caret is in.
 */

export interface MenuItem {
  id: string;
  label: ReactNode;
  onSelect: () => void;
  disabled?: boolean;
  /** Colours a destructive or otherwise notable action. */
  tone?: StatusTone;
  /** Shown greyed on the right — a shortcut, a count, a unit. */
  hint?: string;
}

export function Menu({
  label,
  items,
  align = 'end',
  testId,
}: {
  /** The trigger's text. It is also the list's accessible name. */
  label: string;
  items: readonly MenuItem[];
  align?: 'start' | 'end';
  testId?: string;
}): ReactNode {
  const [open, setOpen] = useState(false);
  const [highlighted, setHighlighted] = useState(0);
  const trigger = useRef<HTMLButtonElement | null>(null);
  const list = useRef<HTMLDivElement | null>(null);
  const listId = useId();

  const enabled = items.filter((item) => item.disabled !== true);

  useEffect(() => {
    if (!open) {
      return;
    }

    list.current?.focus();

    const onPointerDown = (event: MouseEvent): void => {
      const target = event.target;

      if (
        target instanceof Node &&
        list.current?.contains(target) !== true &&
        trigger.current?.contains(target) !== true
      ) {
        setOpen(false);
      }
    };

    document.addEventListener('mousedown', onPointerDown);

    return () => document.removeEventListener('mousedown', onPointerDown);
  }, [open]);

  const close = (restoreFocus: boolean): void => {
    setOpen(false);

    if (restoreFocus) {
      trigger.current?.focus();
    }
  };

  const choose = (item: MenuItem | undefined): void => {
    if (item === undefined || item.disabled === true) {
      return;
    }

    close(true);
    item.onSelect();
  };

  const move = (step: number): void => {
    if (enabled.length === 0) {
      return;
    }

    setHighlighted((current) => (current + step + enabled.length) % enabled.length);
  };

  return (
    <div className="relative inline-flex">
      <button
        ref={trigger}
        type="button"
        data-testid={testId}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={() => {
          setHighlighted(0);
          setOpen((current) => !current);
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            setHighlighted(event.key === 'ArrowDown' ? 0 : Math.max(enabled.length - 1, 0));
            setOpen(true);
          }
        }}
        className={cx(
          'inline-flex h-8 items-center gap-1.5 rounded border border-line-strong bg-raised px-2.5',
          'text-base font-medium whitespace-nowrap text-fg transition-colors hover:border-accent',
        )}
      >
        {label}
        <ChevronIcon className={cx('size-3.5 transition-transform', open ? '-rotate-90' : 'rotate-90')} />
      </button>

      {open && (
        <div
          ref={list}
          id={listId}
          role="menu"
          tabIndex={-1}
          aria-label={label}
          aria-activedescendant={enabled[highlighted] === undefined ? undefined : `${listId}-${highlighted}`}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.preventDefault();
              event.stopPropagation();
              close(true);
            } else if (event.key === 'Tab') {
              close(false);
            } else if (event.key === 'ArrowDown') {
              event.preventDefault();
              move(1);
            } else if (event.key === 'ArrowUp') {
              event.preventDefault();
              move(-1);
            } else if (event.key === 'Home') {
              event.preventDefault();
              setHighlighted(0);
            } else if (event.key === 'End') {
              event.preventDefault();
              setHighlighted(Math.max(enabled.length - 1, 0));
            } else if (event.key === 'Enter' || event.key === ' ') {
              event.preventDefault();
              choose(enabled[highlighted]);
            }
          }}
          className={cx(
            'absolute top-9 z-40 min-w-44 overflow-hidden rounded border border-line-strong bg-panel py-1 shadow-panel',
            align === 'end' ? 'right-0' : 'left-0',
          )}
        >
          {items.map((item) => {
            const index = enabled.indexOf(item);
            const active = index !== -1 && index === highlighted;

            return (
              <div
                key={item.id}
                id={index === -1 ? undefined : `${listId}-${index}`}
                role="menuitem"
                aria-disabled={item.disabled}
                onMouseMove={() => index !== -1 && setHighlighted(index)}
                onClick={() => choose(item)}
                className={cx(
                  'flex cursor-pointer items-center justify-between gap-4 px-3 py-1.5 text-base',
                  item.disabled === true && 'cursor-not-allowed opacity-50',
                  item.tone === undefined ? 'text-fg' : STATUS_TEXT[item.tone],
                  active && 'bg-raised',
                )}
              >
                <span className="truncate">{item.label}</span>
                {item.hint !== undefined && <span className="font-mono text-xs text-subtle">{item.hint}</span>}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
