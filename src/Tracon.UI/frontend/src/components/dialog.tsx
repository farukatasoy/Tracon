import { useCallback, useEffect, useId, useRef, type ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { CrossIcon } from './icons';
import { cx } from './ui';

/**
 * The modal layer.
 *
 * 🚨 Every modal in the console goes through here. Written by hand, once,
 * because the console ships no headless UI dependency — and the four things a
 * modal has to get right are exactly the four things a hand-rolled one forgets:
 *
 *   1. focus moves INTO the dialog when it opens;
 *   2. Tab cannot leave it while it is open;
 *   3. Esc closes it;
 *   4. focus returns to whatever opened it.
 *
 * `UiTests.Command_palette_takes_focus_traps_Tab_and_returns_focus_to_its_trigger_on_Escape`
 * and `UiTests.Shortcut_help_dialog_opens_with_the_question_mark_and_closes_on_Escape`
 * prove all four in a real browser. A screen that draws its own `fixed inset-0`
 * panel is a bug, not a style choice.
 */

/**
 * Elements that can hold focus.
 *
 * `:not([disabled])` and the negative-tabindex exclusion matter: a disabled
 * button and a programmatically-focusable container are both reachable by
 * `.focus()` but neither is a Tab stop, so treating them as one puts the cycle
 * on an element the user can never reach with the keyboard.
 */
const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
  'textarea:not([disabled]), summary, [tabindex]:not([tabindex="-1"])';

function focusableWithin(container: HTMLElement): HTMLElement[] {
  return [...container.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
    (element) => element.offsetParent !== null || element === document.activeElement,
  );
}

/**
 * Traps Tab inside `container` and restores focus when it goes away.
 *
 * Returns the keydown handler the container has to carry. The handler is on the
 * container rather than the window on purpose: a window listener would also
 * swallow Tab for any OTHER dialog stacked on top of this one.
 */
export function useFocusTrap(
  open: boolean,
  container: React.RefObject<HTMLElement | null>,
  onClose: () => void,
): (event: React.KeyboardEvent) => void {
  const trigger = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) {
      return;
    }

    trigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    const element = container.current;

    if (element !== null) {
      // The first real control, or the panel itself when there is none — never
      // <body>, which would strand the next Tab back at the top of the page.
      (focusableWithin(element)[0] ?? element).focus();
    }

    // A modal that lets the page behind it scroll is a modal the user can lose.
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    return () => {
      document.body.style.overflow = previousOverflow;
      trigger.current?.focus();
      trigger.current = null;
    };
  }, [container, open]);

  return useCallback(
    (event: React.KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        // Esc closes ONE layer. The event must not reach the window handler,
        // which would close the shell's overlays underneath this one too.
        event.stopPropagation();
        onClose();

        return;
      }

      if (event.key !== 'Tab') {
        return;
      }

      const element = container.current;

      if (element === null) {
        return;
      }

      const stops = focusableWithin(element);

      if (stops.length === 0) {
        event.preventDefault();

        return;
      }

      const first = stops[0];
      const last = stops[stops.length - 1];
      const active = document.activeElement;

      if (event.shiftKey && (active === first || active === element)) {
        event.preventDefault();
        last?.focus();
      } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first?.focus();
      }
    },
    [container, onClose],
  );
}

export function Dialog({
  open,
  onClose,
  title,
  description,
  children,
  footer,
  width = 'max-w-lg',
  testId,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: string;
  children: ReactNode;
  footer?: ReactNode;
  /** Tailwind max-width class. Dialogs are content-sized, not screen-sized. */
  width?: string;
  testId?: string;
}): ReactNode {
  const t = useT();
  const panel = useRef<HTMLDivElement | null>(null);
  const titleId = useId();
  const descriptionId = useId();
  const onKeyDown = useFocusTrap(open, panel, onClose);

  if (!open) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/55 px-4 py-8 backdrop-blur-[2px]"
      onMouseDown={(event) => {
        // `mousedown`, not `click`: a drag that starts inside the dialog and
        // ends on the backdrop is a text selection, not a dismissal.
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description === undefined ? undefined : descriptionId}
        data-testid={testId}
        tabIndex={-1}
        onKeyDown={onKeyDown}
        className={cx(
          'flex max-h-full w-full flex-col overflow-hidden rounded-md border border-line-strong bg-panel shadow-panel',
          width,
        )}
      >
        <div className="flex items-start gap-3 border-b border-line px-4 py-3">
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-section font-semibold tracking-tight">
              {title}
            </h2>
            {description !== undefined && (
              <p id={descriptionId} className="mt-1 text-sm text-muted">
                {description}
              </p>
            )}
          </div>

          <button
            type="button"
            onClick={onClose}
            aria-label={t('common.close')}
            data-testid="dialog-close"
            className="-mr-1 rounded p-1 text-subtle transition-colors hover:bg-raised hover:text-fg"
          >
            <CrossIcon className="size-4" />
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto">{children}</div>

        {footer !== undefined && (
          <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line px-4 py-3">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
