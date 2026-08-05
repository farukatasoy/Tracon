import { useState, type InputHTMLAttributes, type ReactNode, type TextareaHTMLAttributes } from 'react';
import { useT } from '../lib/i18n';
import { CheckIcon, CopyIcon, SpinnerIcon } from './icons';

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
    <header className="mb-5 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {description !== undefined && (
          <p className="mt-1 max-w-2xl text-[13px] text-muted">{description}</p>
        )}
      </div>
      {actions !== undefined && <div className="flex items-center gap-2">{actions}</div>}
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
    <section
      className={cx('rounded-lg border border-line bg-panel shadow-panel', className)}
    >
      {(title !== undefined || actions !== undefined) && (
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
          <h2 className="text-[13px] font-semibold tracking-tight">{title}</h2>
          {actions !== undefined && <div className="flex items-center gap-1.5">{actions}</div>}
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
  className,
  testId,
}: {
  children: ReactNode;
  onClick?: () => void;
  tone?: ButtonTone;
  type?: 'button' | 'submit';
  disabled?: boolean;
  busy?: boolean;
  title?: string;
  className?: string;
  testId?: string;
}): ReactNode {
  const tones: Record<ButtonTone, string> = {
    primary: 'bg-accent text-accent-fg hover:bg-accent-hover border-transparent',
    default: 'bg-raised text-fg hover:border-line-strong border-line',
    ghost: 'bg-transparent text-muted hover:text-fg hover:bg-raised border-transparent',
    danger: 'bg-transparent text-danger hover:bg-danger-soft border-line',
  };

  return (
    <button
      type={type}
      title={title}
      data-testid={testId}
      disabled={disabled === true || busy === true}
      onClick={onClick}
      className={cx(
        'inline-flex h-8 items-center gap-1.5 rounded-md border px-3 text-[13px] font-medium whitespace-nowrap',
        'transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        tones[tone],
        className,
      )}
    >
      {busy === true && <SpinnerIcon className="size-3.5" />}
      {children}
    </button>
  );
}

export function Field({
  label,
  hint,
  children,
  required,
}: {
  label: string;
  hint?: ReactNode;
  children: ReactNode;
  required?: boolean;
}): ReactNode {
  return (
    <label className="block">
      <span className="mb-1 flex items-baseline gap-1.5 text-[12px] font-medium text-muted">
        {label}
        {required === true && <span className="text-danger">*</span>}
      </span>
      {children}
      {hint !== undefined && <span className="mt-1 block text-[11px] text-subtle">{hint}</span>}
    </label>
  );
}

const controlClass =
  'w-full rounded-md border border-line bg-panel px-2.5 py-1.5 text-[13px] text-fg ' +
  'placeholder:text-subtle focus:border-accent focus:outline-none';

export function TextInput(props: InputHTMLAttributes<HTMLInputElement>): ReactNode {
  return <input {...props} className={cx(controlClass, props.className)} />;
}

export function TextArea(props: TextareaHTMLAttributes<HTMLTextAreaElement>): ReactNode {
  return <textarea {...props} className={cx(controlClass, 'resize-y font-mono', props.className)} />;
}

export function Select({
  value,
  onChange,
  children,
  disabled,
  testId,
}: {
  value: string;
  onChange: (value: string) => void;
  children: ReactNode;
  disabled?: boolean;
  testId?: string;
}): ReactNode {
  return (
    <select
      value={value}
      disabled={disabled}
      data-testid={testId}
      onChange={(event) => onChange(event.target.value)}
      className={cx(controlClass, 'disabled:opacity-50')}
    >
      {children}
    </select>
  );
}

/* ------------------------------------------------------------------ signals */

type BadgeTone = 'neutral' | 'accent' | 'success' | 'danger' | 'warn' | 'info';

export function Badge({
  children,
  tone = 'neutral',
  title,
}: {
  children: ReactNode;
  tone?: BadgeTone;
  title?: string;
}): ReactNode {
  const tones: Record<BadgeTone, string> = {
    neutral: 'bg-raised text-muted border-line',
    accent: 'bg-accent-soft text-accent border-transparent',
    success: 'bg-success-soft text-success border-transparent',
    danger: 'bg-danger-soft text-danger border-transparent',
    warn: 'bg-warn-soft text-warn border-transparent',
    info: 'bg-info-soft text-info border-transparent',
  };

  return (
    <span
      title={title}
      className={cx(
        'inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[11px] font-medium',
        tones[tone],
      )}
    >
      {children}
    </span>
  );
}

export function Mono({
  children,
  className,
  title,
}: {
  children: ReactNode;
  className?: string;
  title?: string;
}): ReactNode {
  return (
    <span title={title} className={cx('font-mono text-[12px]', className)}>
      {children}
    </span>
  );
}

export function Empty({ title, children }: { title: string; children?: ReactNode }): ReactNode {
  return (
    <div className="px-4 py-12 text-center">
      <p className="text-[13px] font-medium">{title}</p>
      {children !== undefined && (
        <div className="mx-auto mt-1.5 max-w-md text-[12px] text-muted">{children}</div>
      )}
    </div>
  );
}

export function Loading({ label }: { label?: string }): ReactNode {
  const t = useT();

  return (
    <div
      role="status"
      aria-live="polite"
      className="flex items-center justify-center gap-2 px-4 py-12 text-[13px] text-muted"
    >
      <SpinnerIcon />
      {label ?? t('common.loading')}
    </div>
  );
}

export function ErrorNote({ error }: { error: unknown }): ReactNode {
  const message = error instanceof Error ? error.message : String(error);

  // 🚨 The text is NOT translated. It comes from the server, whose API contract
  // is single-language on purpose; inventing a Turkish sentence for a message
  // the console does not recognise would hide what actually failed.
  return (
    <div
      role="alert"
      className="rounded-md border border-line bg-danger-soft px-3 py-2 text-[12px] text-danger"
    >
      {message}
    </div>
  );
}

/* ------------------------------------------------------------------ tables */

export function Table({ children }: { children: ReactNode }): ReactNode {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-[13px]">{children}</table>
    </div>
  );
}

export function Th({ children, className }: { children?: ReactNode; className?: string }): ReactNode {
  return (
    <th
      className={cx(
        'border-b border-line px-4 py-2 text-left text-[11px] font-semibold tracking-wide text-subtle uppercase',
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
    <td title={title} className={cx('border-b border-line px-4 py-2 align-middle', className)}>
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
        className={cx(
          'overflow-auto rounded-md border border-line bg-raised p-3 font-mono text-[12px] leading-relaxed',
          maxHeight,
        )}
      >
        {code}
      </pre>
    </div>
  );
}

export function CopyButton({ value }: { value: string }): ReactNode {
  const t = useT();
  const [copied, setCopied] = useState(false);

  return (
    <button
      type="button"
      title={t('common.copy')}
      aria-label={t('common.copy')}
      className="absolute top-1.5 right-1.5 z-10 rounded border border-line bg-panel p-1 text-muted hover:text-fg"
      onClick={() => {
        void navigator.clipboard.writeText(value).then(() => {
          setCopied(true);
          window.setTimeout(() => setCopied(false), 1200);
        });
      }}
    >
      {copied ? <CheckIcon className="size-3.5 text-success" /> : <CopyIcon className="size-3.5" />}
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
