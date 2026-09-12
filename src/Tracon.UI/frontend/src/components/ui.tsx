import {
  useId,
  useState,
  type InputHTMLAttributes,
  type ReactNode,
  type TextareaHTMLAttributes,
} from 'react';
import { useT } from '../lib/i18n';
import { CheckIcon, CopyIcon, LockIcon } from './icons';
import { STATUS_CHIP, type StatusTone } from './status-dot';

/**
 * The console's primitives.
 *
 * Two rules hold this file together:
 *
 *  1. **Sizes come from the scale.** `text-base`, `text-xs`, `text-id` — the
 *     steps defined in `styles.css`. A `text-[13px]` here is the scale leaking
 *     back out into components, which is the state this layer replaced.
 *  2. **Colours come from the token set**, and a status colour comes from
 *     `status-dot.tsx`, never from a literal.
 *
 * Call sites are the constraint: thirty screens use these, so props are ADDED,
 * never renamed or removed.
 */

export function cx(...parts: (string | false | null | undefined)[]): string {
  return parts.filter(Boolean).join(' ');
}

/* ------------------------------------------------------------------ layout */

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
}): ReactNode {
  return (
    <header className="mb-4 flex flex-wrap items-start justify-between gap-3 border-b border-line pb-3">
      <div className="min-w-0">
        <h1 className="text-title font-semibold tracking-tight">{title}</h1>
        {description !== undefined && (
          <p className="mt-1 max-w-2xl text-base text-muted">{description}</p>
        )}
      </div>
      {actions !== undefined && <div className="flex flex-wrap items-end gap-2">{actions}</div>}
    </header>
  );
}

export function Panel({
  children,
  className,
  title,
  actions,
}: {
  children: ReactNode;
  className?: string;
  title?: ReactNode;
  actions?: ReactNode;
}): ReactNode {
  return (
    // 🚨 `min-w-0`: a panel is almost always a grid or flex item, and such an
    // item defaults to `min-width: auto` — it refuses to shrink below the
    // widest thing inside it. One wide chart or one long identifier then pushes
    // the whole COLUMN past the viewport, which is how a narrow screen ends up
    // scrolling sideways. The inner scroll containers (tables, code blocks)
    // handle their own overflow.
    <section className={cx('min-w-0 rounded border border-line bg-panel shadow-panel', className)}>
      {(title !== undefined || actions !== undefined) && (
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-2">
          <h2 className="min-w-0 text-section font-semibold tracking-tight">{title}</h2>
          {actions !== undefined && (
            <div className="flex flex-wrap items-center gap-1.5">{actions}</div>
          )}
        </div>
      )}
      {children}
    </section>
  );
}

/* ------------------------------------------------------------------ inputs */

type ButtonTone = 'primary' | 'default' | 'ghost' | 'danger';

export function Button({
  children,
  onClick,
  tone = 'default',
  type = 'button',
  disabled,
  busy,
  title,
  ariaLabel,
  className,
  testId,
  'aria-describedby': describedBy,
}: {
  children: ReactNode;
  onClick?: () => void;
  tone?: ButtonTone;
  type?: 'button' | 'submit';
  disabled?: boolean;
  busy?: boolean;
  title?: string;
  /** Required when the button's content is an icon and nothing else. */
  ariaLabel?: string;
  className?: string;
  testId?: string;
  /**
   * 🚨 Declared explicitly because this component renders a FIXED attribute
   * list. `Tooltip` and `Field` generate this binding and hand it down; a
   * primitive that quietly drops an unknown prop turns an accessible control
   * into an inaccessible one with no error anywhere. `ui.test.tsx` asserts it
   * lands on the DOM.
   */
  'aria-describedby'?: string;
}): ReactNode {
  const tones: Record<ButtonTone, string> = {
    primary: 'bg-accent text-accent-fg hover:bg-accent-hover border-transparent font-semibold',
    default: 'bg-raised text-fg border-line-strong hover:border-accent',
    ghost: 'bg-transparent text-muted hover:text-fg hover:bg-raised border-transparent',
    danger: 'bg-transparent text-danger border-line-strong hover:bg-danger-soft hover:border-danger',
  };

  return (
    <button
      type={type}
      title={title}
      aria-label={ariaLabel}
      aria-describedby={describedBy}
      aria-busy={busy === true ? true : undefined}
      data-testid={testId}
      disabled={disabled === true || busy === true}
      onClick={onClick}
      className={cx(
        'inline-flex h-8 items-center gap-1.5 rounded border px-2.5 text-base font-medium whitespace-nowrap',
        'transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        tones[tone],
        className,
      )}
    >
      {busy === true && <Spinner />}
      {children}
    </button>
  );
}

/**
 * The busy marker inside a control.
 *
 * A whole SCREEN loading is a skeleton (`Loading`); a control that is waiting
 * for one request keeps its own size and spins, because there is no layout to
 * hold open.
 */
function Spinner(): ReactNode {
  return (
    <span
      aria-hidden="true"
      className="size-3 shrink-0 animate-spin rounded-full border-[1.5px] border-current border-t-transparent"
    />
  );
}

export interface FieldIds {
  /** Put on the control. */
  id: string;
  /** Put on the control; names the hint and the error. */
  'aria-describedby': string | undefined;
  'aria-invalid': boolean | undefined;
}

export function Field({
  label,
  hint,
  children,
  required,
  error,
}: {
  label: string;
  hint?: ReactNode;
  /**
   * A control, or a function that receives the ids to put on it. The function
   * form is what binds the hint and the error message to the control; the plain
   * form relies on the wrapping `<label>` alone.
   */
  children: ReactNode | ((ids: FieldIds) => ReactNode);
  required?: boolean;
  /** A validation message. Announced, and it marks the control invalid. */
  error?: string;
}): ReactNode {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const described = [hint === undefined ? null : hintId, error === undefined ? null : errorId]
    .filter(Boolean)
    .join(' ');

  const control =
    typeof children === 'function'
      ? children({
          id,
          'aria-describedby': described.length === 0 ? undefined : described,
          'aria-invalid': error === undefined ? undefined : true,
        })
      : children;

  const caption = (
    <>
      <span className="mb-1 flex items-baseline gap-1.5 text-xs font-medium text-muted">
        {label}
        {/* 🚨 NOT `aria-hidden`. The asterisk is part of the field's accessible
            name, which is how every caller and every test refers to it, and
            hiding it would announce a required field as optional unless every
            call site also passed `aria-required` — which the plain-children
            form of this component cannot do. */}
        {required === true && <span className="text-danger">*</span>}
      </span>
      {control}
      {hint !== undefined && (
        <span id={hintId} className="mt-1 block text-xs text-subtle">
          {hint}
        </span>
      )}
      {error !== undefined && (
        <span id={errorId} role="alert" className="mt-1 block text-xs text-danger">
          {error}
        </span>
      )}
    </>
  );

  // With the render-prop form the control carries its own id, so a wrapping
  // <label> would be a second, conflicting association; `htmlFor` is used
  // instead. Without it the implicit association is all there is.
  return typeof children === 'function' ? (
    <div className="block">
      <label htmlFor={id} className="contents">
        {caption}
      </label>
    </div>
  ) : (
    <label className="block">{caption}</label>
  );
}

const controlClass =
  'w-full rounded border border-line-strong bg-panel px-2 py-1.5 text-base text-fg ' +
  'placeholder:text-subtle focus:border-accent focus:outline-none ' +
  'aria-[invalid=true]:border-danger';

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>): ReactNode {
  return <input {...props} className={cx(controlClass, props.className)} />;
}

export function TextArea(props: TextareaHTMLAttributes<HTMLTextAreaElement>): ReactNode {
  return (
    <textarea
      {...props}
      className={cx(controlClass, 'resize-y font-mono text-id leading-relaxed', props.className)}
    />
  );
}

export function Select({
  value,
  onChange,
  children,
  disabled,
  testId,
  id,
  ariaLabel,
  'aria-describedby': describedBy,
  'aria-invalid': invalid,
}: {
  value: string;
  /** 🚨 A raw string, NOT a DOM event — see `docs/hafiza/frontend.md`. */
  onChange: (value: string) => void;
  children: ReactNode;
  disabled?: boolean;
  testId?: string;
  /** Binds the control to an external `<label htmlFor>`. */
  id?: string;
  /** Use when no visible label exists. A `<select>`'s options are not its name. */
  ariaLabel?: string;
  /** Same reason as `Button`: this component renders a FIXED attribute list. */
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
}): ReactNode {
  return (
    <select
      id={id}
      value={value}
      disabled={disabled}
      aria-label={ariaLabel}
      aria-describedby={describedBy}
      aria-invalid={invalid}
      data-testid={testId}
      onChange={(event) => onChange(event.target.value)}
      className={cx(controlClass, 'h-8 py-0 disabled:opacity-50')}
    >
      {children}
    </select>
  );
}

/* ------------------------------------------------------------------ signals */

export function Badge({
  children,
  tone = 'neutral',
  title,
}: {
  children: ReactNode;
  tone?: StatusTone;
  title?: string;
}): ReactNode {
  return (
    <span
      title={title}
      className={cx(
        'inline-flex items-center gap-1 rounded-sm border px-1.5 py-px text-xs font-medium whitespace-nowrap',
        STATUS_CHIP[tone],
      )}
    >
      {children}
    </span>
  );
}

/**
 * An identifier.
 *
 * 🚨 Every identifier in this console — run, session, tenant, span, key prefix —
 * is monospace and the same size, so a operator can compare two of them by
 * shape. `copyable` puts a copy control next to it; an id that has to be
 * retyped by hand is an id that will be retyped wrong.
 */
export function Mono({
  children,
  className,
  title,
  copy,
}: {
  children: ReactNode;
  className?: string;
  title?: string;
  /** The full value to copy. Usually the untruncated id. */
  copy?: string;
}): ReactNode {
  const text = (
    <span title={title} className={cx('font-mono text-id', className)}>
      {children}
    </span>
  );

  if (copy === undefined) {
    return text;
  }

  return (
    <span className="inline-flex items-center gap-1">
      {text}
      <CopyButton value={copy} variant="inline" />
    </span>
  );
}

/* ------------------------------------------------------------------ states */

/**
 * The four states every screen can be in — empty, loading, failed, refused —
 * have one telling each. They were four different tellings per screen before.
 */

export function Empty({
  title,
  children,
  action,
}: {
  title: string;
  children?: ReactNode;
  /** 🚨 An empty state says what is not there AND how to create the first one. */
  action?: ReactNode;
}): ReactNode {
  return (
    <div className="px-4 py-10 text-center">
      <p className="text-base font-medium">{title}</p>
      {children !== undefined && (
        <div className="mx-auto mt-1.5 max-w-md text-sm text-muted">{children}</div>
      )}
      {action !== undefined && <div className="mt-3 flex justify-center gap-2">{action}</div>}
    </div>
  );
}

/**
 * Loading.
 *
 * 🚨 A skeleton, not a spinner: it holds the height the rows will take, so the
 * page does not jump when they arrive. `rows` should match what the caller is
 * about to render — a table asks for its page size, a panel for one or two.
 */
export function Loading({ label, rows = 4 }: { label?: string; rows?: number }): ReactNode {
  const t = useT();

  return (
    <div role="status" aria-live="polite" aria-busy="true" className="flex flex-col gap-2 px-4 py-3">
      <span className="sr-only">{label ?? t('common.loading')}</span>
      {Array.from({ length: rows }, (_, index) => (
        <span
          key={index}
          aria-hidden="true"
          className="tracon-skeleton block h-5"
          // A ragged right edge reads as text; equal bars read as a progress
          // bar the user will wait on.
          style={{ width: `${[96, 78, 88, 64, 82, 71][index % 6] ?? 80}%` }}
        />
      ))}
    </div>
  );
}

export function ErrorNote({ error, onRetry }: { error: unknown; onRetry?: () => void }): ReactNode {
  const t = useT();
  const message = error instanceof Error ? error.message : String(error);

  // 🚨 The text is NOT translated. It comes from the server, whose API contract
  // is single-language on purpose; inventing a Turkish sentence for a message
  // the console does not recognise would hide what actually failed.
  return (
    <div
      role="alert"
      className="flex flex-wrap items-center justify-between gap-2 rounded border border-danger bg-danger-soft px-3 py-2 text-sm text-danger"
    >
      <span className="min-w-0 break-words">{message}</span>
      {onRetry !== undefined && (
        <Button tone="ghost" onClick={onRetry} testId="error-retry">
          {t('common.retry')}
        </Button>
      )}
    </div>
  );
}

/**
 * Refused, not empty.
 *
 * 🚨 A reader who opens an admin screen used to see the empty state and
 * conclude there was nothing there. This says the opposite: there IS something
 * here, and your role does not reach it. The server is the enforcement; this is
 * only the explanation (`docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md`).
 */
export function Unauthorized({ requires }: { requires: 'operator' | 'administrator' }): ReactNode {
  const t = useT();

  return (
    <div className="px-4 py-10 text-center" data-testid="unauthorized">
      <LockIcon className="mx-auto size-5 text-subtle" />
      <p className="mt-2 text-base font-medium">{t('state.unauthorized.title')}</p>
      <p className="mx-auto mt-1.5 max-w-md text-sm text-muted">
        {t(`state.unauthorized.requires.${requires}`)}
      </p>
    </div>
  );
}

/* ------------------------------------------------------------------ tables */

export function Table({ children, label }: { children: ReactNode; label?: string }): ReactNode {
  return (
    // A horizontally scrollable region has to be reachable by keyboard, or the
    // columns past the fold cannot be read without a mouse.
    <div tabIndex={0} role="group" aria-label={label} className="overflow-x-auto">
      <table className="w-full border-collapse text-base">{children}</table>
    </div>
  );
}

export function Th({ children, className }: { children?: ReactNode; className?: string }): ReactNode {
  return (
    <th
      scope="col"
      className={cx(
        'border-b border-line bg-raised px-3 py-1.5 text-left text-2xs font-semibold tracking-wider text-subtle uppercase',
        className,
      )}
    >
      {children}
    </th>
  );
}

export function Td({
  children,
  className,
  title,
}: {
  children?: ReactNode;
  className?: string;
  title?: string;
}): ReactNode {
  return (
    <td title={title} className={cx('border-b border-line px-3 py-1.5 align-middle', className)}>
      {children}
    </td>
  );
}

/* ------------------------------------------------------------------ json */

export function CodeBlock({
  code,
  className,
  maxHeight = 'max-h-96',
}: {
  code: string;
  className?: string;
  maxHeight?: string;
}): ReactNode {
  return (
    <div className={cx('relative', className)}>
      <CopyButton value={code} />
      <pre
        tabIndex={0}
        className={cx(
          'overflow-auto rounded border border-line bg-raised p-3 font-mono text-id leading-relaxed',
          maxHeight,
        )}
      >
        {code}
      </pre>
    </div>
  );
}

export function CopyButton({
  value,
  variant = 'overlay',
}: {
  value: string;
  /** `inline` sits in the text flow, next to an identifier. */
  variant?: 'overlay' | 'inline';
}): ReactNode {
  const t = useT();
  const [copied, setCopied] = useState(false);

  return (
    <button
      type="button"
      title={copied ? t('common.copied') : t('common.copy')}
      aria-label={t('common.copy')}
      className={cx(
        'rounded text-subtle transition-colors hover:text-fg',
        variant === 'overlay'
          ? 'absolute top-1.5 right-1.5 z-10 border border-line bg-panel p-1'
          : 'p-0.5 opacity-60 hover:opacity-100',
      )}
      onClick={() => {
        void navigator.clipboard.writeText(value).then(() => {
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1200);
        });
      }}
    >
      {copied ? (
        <CheckIcon className="size-3.5 text-success" />
      ) : (
        <CopyIcon className={variant === 'overlay' ? 'size-3.5' : 'size-3'} />
      )}
      <span aria-live="polite" className="sr-only">
        {copied ? t('common.copied') : ''}
      </span>
    </button>
  );
}

/** Renders any JSON-serialisable value as a formatted, copyable block. */
export function JsonView({ value, maxHeight }: { value: unknown; maxHeight?: string }): ReactNode {
  let text: string;

  try {
    text = JSON.stringify(value, null, 2) ?? 'null';
  } catch {
    text = String(value);
  }

  return <CodeBlock code={text} maxHeight={maxHeight} />;
}
