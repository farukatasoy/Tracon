import { cloneElement, useId, useState, type ReactElement, type ReactNode } from 'react';
import { cx } from './ui';

/**
 * A description attached to a control.
 *
 * 🚨 This replaces the `title` attribute, which was the console's tooltip
 * everywhere and is not a tooltip at all: `title` never appears on a touch
 * device, never appears for a keyboard user who tabbed to the control, and is
 * read inconsistently by screen readers. This component shows on hover AND on
 * focus, dismisses on Esc, and — the part `title` cannot do — binds the text to
 * the control with `aria-describedby`.
 *
 * The bubble is rendered at all times and merely moved off-screen while it is
 * hidden. An `aria-describedby` pointing at an element that only exists on
 * hover describes nothing the rest of the time.
 */
export function Tooltip({
  text,
  placement = 'top',
  children,
}: {
  text: string;
  placement?: 'top' | 'bottom';
  /** A single focusable element — a button, a link, an input. */
  children: ReactElement<{ 'aria-describedby'?: string }>;
}): ReactNode {
  const id = useId();
  const [visible, setVisible] = useState(false);

  return (
    <span
      className="relative inline-flex"
      onMouseEnter={() => setVisible(true)}
      onMouseLeave={() => setVisible(false)}
      // React's focus/blur are the delegated focusin/focusout pair, so they do
      // reach here from the child element.
      onFocus={() => setVisible(true)}
      onBlur={() => setVisible(false)}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && visible) {
          // Dismissible without moving the pointer (WCAG 1.4.13). It does NOT
          // stop propagation: Esc still closes the layer above this one.
          setVisible(false);
        }
      }}
    >
      {cloneElement(children, { 'aria-describedby': id })}

      <span
        id={id}
        role="tooltip"
        className={cx(
          'pointer-events-none z-40 w-max max-w-56 rounded border border-line-strong bg-raised px-2 py-1',
          'text-xs leading-snug text-fg shadow-panel',
          visible
            ? cx(
                'absolute left-1/2 -translate-x-1/2',
                placement === 'top' ? 'bottom-full mb-1.5' : 'top-full mt-1.5',
              )
            : 'sr-only',
        )}
      >
        {text}
      </span>
    </span>
  );
}
