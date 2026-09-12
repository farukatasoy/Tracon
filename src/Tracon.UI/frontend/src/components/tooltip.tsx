import {
  cloneElement,
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactElement,
  type ReactNode,
} from 'react';
import { cx } from '../lib/cx';

/** How close to the viewport edge the bubble may come, in pixels. */
const EDGE_MARGIN = 8;

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
 * The bubble is rendered at all times: an `aria-describedby` pointing at an
 * element that only exists on hover describes nothing the rest of the time.
 * While hidden it carries `sr-only` and NOTHING ELSE — see the class list
 * below for why keeping its sizing classes there was a defect.
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
  const [shift, setShift] = useState(0);
  const host = useRef<HTMLSpanElement>(null);
  const bubble = useRef<HTMLSpanElement>(null);

  /*
    🚨 Why this measurement exists: the bubble is centred on its trigger, so a
    trigger near the right edge pushes 224px of bubble past the viewport and the
    whole PAGE scrolls sideways. Phase 165 spread tooltips onto badges and
    column headings inside tables, which is exactly where a trigger sits near
    the edge — `Remaining_screens_do_not_overflow_horizontally_at_375px_width`
    caught it at 104px of overflow on the models screen.

    It cannot be solved in CSS: clamping needs the trigger's position and the
    bubble's width, and neither is knowable to a stylesheet. The read happens
    only while the bubble is actually shown, so it costs nothing at rest.
  */
  const clamp = useCallback(() => {
    const trigger = host.current;
    const balloon = bubble.current;

    if (trigger === null || balloon === null) {
      return;
    }

    const box = trigger.getBoundingClientRect();
    const width = balloon.offsetWidth;
    const left = box.left + box.width / 2 - width / 2;
    const limit = document.documentElement.clientWidth - EDGE_MARGIN;

    if (left < EDGE_MARGIN) {
      setShift(EDGE_MARGIN - left);
    } else if (left + width > limit) {
      setShift(limit - (left + width));
    } else {
      setShift(0);
    }
  }, []);

  // 🚨 `useLayoutEffect`, not `useEffect`: a passive effect runs AFTER paint, so
  // the bubble would be drawn once uncorrected and the page would scroll
  // sideways for that frame — the exact defect this clamp exists to prevent.
  useLayoutEffect(() => {
    if (!visible) {
      setShift(0);

      return;
    }

    clamp();
  }, [visible, text, clamp]);

  // A bubble open across a resize (or an orientation change on a phone) has to
  // be re-measured; the trigger moved under it.
  useEffect(() => {
    if (!visible) {
      return;
    }

    window.addEventListener('resize', clamp);

    return () => window.removeEventListener('resize', clamp);
  }, [visible, clamp]);

  return (
    <span
      ref={host}
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
        ref={bubble}
        id={id}
        role="tooltip"
        // The translate is inline rather than a utility class because the
        // clamp above has to add to it; two `transform` sources cannot both win.
        //
        // 🚨 The operator is chosen rather than interpolated: `calc(-50% + -112px)`
        // is rejected as a parse error and the whole declaration is dropped, so
        // the clamp silently did nothing and the bubble kept overflowing. The
        // sign has to live in the operator, not in the operand.
        style={
          visible
            ? { transform: `translateX(calc(-50% ${shift < 0 ? '-' : '+'} ${Math.abs(shift)}px))` }
            : undefined
        }
        /*
          🚨 The hidden bubble gets `sr-only` AND NOTHING ELSE. It used to keep
          its sizing classes in both states, and `w-max max-w-56` wins over
          `sr-only`'s `width: 1px` — Tailwind resolves a conflict by stylesheet
          order, not by the order classes appear in the attribute. So a "hidden"
          bubble was still 224px wide and absolutely positioned, which still
          counts toward `scrollWidth`: every tooltip near a right edge was
          pushing the page sideways with a bubble nobody could see. A runtime
          probe found it (the visible bubble measured correctly at the same
          moment the hidden one measured 224px past the edge); reading the class
          list could not.
        */
        className={
          visible
            ? cx(
                'pointer-events-none absolute left-1/2 z-40 w-max max-w-56 rounded',
                'border border-line-strong bg-raised px-2 py-1',
                'text-xs leading-snug text-fg shadow-panel',
                placement === 'top' ? 'bottom-full mb-1.5' : 'top-full mt-1.5',
              )
            : 'sr-only'
        }
      >
        {text}
      </span>
    </span>
  );
}
