/**
 * Class name joining.
 *
 * Its own module rather than a helper inside `components/ui.tsx`, because the
 * primitives import each other: `ui.tsx` needs `status-dot.tsx` for a chip's
 * colours and `tooltip.tsx` for a badge's description, and both of those need
 * `cx`. With `cx` living in `ui.tsx` that was an import cycle — it happened to
 * resolve, because a hoisted function declaration is defined before either
 * module's body runs, but it is a cycle no bundler is obliged to keep working.
 *
 * `ui.tsx` re-exports this so the thirty screens keep importing `cx` from the
 * one place they import every other primitive from.
 */
export function cx(...parts: (string | false | null | undefined)[]): string {
  return parts.filter(Boolean).join(' ');
}
