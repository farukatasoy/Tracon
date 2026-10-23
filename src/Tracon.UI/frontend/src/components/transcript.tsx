import { useState, type ReactNode } from 'react';
import { prettyJson } from '../lib/format';
import { useT } from '../lib/i18n';
import type { InputField, TranscriptItem } from '../lib/transcript';
import { Badge, CodeBlock, cx, Field, Select, TextInput } from './ui';
import { CheckIcon, ChevronIcon, CrossIcon, SpinnerIcon } from './icons';

/**
 * Renders folded transcript blocks.
 *
 * A tool call is the interesting part of an agent run, so it gets a card of its
 * own with arguments and result rather than being flattened into the text.
 */
export function TranscriptView({
  items,
  streaming,
  onDecide,
  onAnswer,
}: {
  items: readonly TranscriptItem[];
  streaming?: boolean;
  /**
   * Called when the operator answers a pending approval. Absent on read-only
   * views (a recorded run), where the decision has already been made or the
   * turn can no longer be continued.
   */
  onDecide?: (requestId: string, approved: boolean, remember: boolean) => void;
  /**
   * Called when the user answers a tool's request for input. Absent for the same
   * reason as `onDecide`.
   */
  onAnswer?: (requestId: string, action: InputAction, content: Record<string, unknown> | null) => void;
}): ReactNode {
  return (
    <div className="flex flex-col gap-2.5">
      {items.map((item, index) => {
        const isLast = index === items.length - 1;

        switch (item.kind) {
          case 'text':
            return (
              <p
                key={item.id}
                className={cx(
                  'text-base leading-relaxed whitespace-pre-wrap',
                  // 🚨 The name has to be the one styles.css defines. This
                  // read `ap-stream-caret`, a leftover of the rename that
                  // turned the `ap-` prefix into `tracon-`, and no rule
                  // matched: the class was applied on every streaming turn and
                  // the caret never appeared. Nothing broke, so 79 E2E cases
                  // stayed green - `check-custom-classes.mjs` is the gate.
                  streaming === true && isLast && 'tracon-stream-caret',
                )}
              >
                {item.text}
              </p>
            );

          case 'reasoning':
            return <ReasoningBlock key={item.id} text={item.text} />;

          case 'error':
            return (
              <div
                key={item.id}
                className="rounded-md border border-line bg-danger-soft px-3 py-2 text-sm text-danger"
              >
                {item.message}
              </div>
            );

          case 'compaction':
            return (
              <div
                key={item.id}
                className="rounded-md border border-line bg-accent-soft px-3 py-2 text-sm text-accent"
                title={item.detail ?? undefined}
              >
                {item.message}
              </div>
            );

          case 'approval':
            return <ApprovalCard key={item.id} item={item} onDecide={onDecide} />;

          case 'input':
            return <InputCard key={item.id} item={item} onAnswer={onAnswer} />;

          case 'tool':
            return <ToolCard key={item.id} item={item} />;

          default:
            return null;
        }
      })}
    </div>
  );
}

function ReasoningBlock({ text }: { text: string }): ReactNode {
  const t = useT();
  const [open, setOpen] = useState(false);

  return (
    <div className="rounded-md border border-line bg-raised">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-1.5 px-3 py-1.5 text-left text-sm text-muted"
      >
        <ChevronIcon className={cx('size-3 transition-transform', open && 'rotate-90')} />
        {t('transcript.reasoning')}
      </button>
      {open && (
        <p className="px-3 pb-3 text-sm leading-relaxed whitespace-pre-wrap text-muted">{text}</p>
      )}
    </div>
  );
}

/**
 * A tool call waiting for a decision.
 *
 * The run has already ended at this point: Microsoft Agent Framework returns
 * the request instead of running the tool, and the answer is carried as the
 * input of the next turn. That is why this card submits a new run rather than
 * resuming a paused one.
 */
function ApprovalCard({
  item,
  onDecide,
}: {
  item: Extract<TranscriptItem, { kind: 'approval' }>;
  onDecide?: (requestId: string, approved: boolean, remember: boolean) => void;
}): ReactNode {
  const t = useT();
  const [remember, setRemember] = useState(false);

  const [argsOpen, setArgsOpen] = useState(false);

  return (
    <div
      data-testid="approval-card"
      className="overflow-hidden rounded-md border border-line bg-panel"
      style={{ borderLeft: '2px solid var(--tracon-warn)' }}
    >
      <div className="flex items-center gap-2 px-3 py-2">
        {item.entityName === null ? (
          <span className="font-mono text-sm font-medium">{item.name}</span>
        ) : (
          <span className="flex flex-col">
            <span className="text-sm font-medium">{item.entityName}</span>
            <span className="font-mono text-2xs text-subtle">{item.name}</span>
          </span>
        )}
        {item.decided === null ? (
          <Badge tone="warn">{t('tools.approvalRequired')}</Badge>
        ) : item.decided === 'approved' ? (
          <Badge tone="success">
            <CheckIcon className="size-3" />
            {t('transcript.approved')}
          </Badge>
        ) : (
          <Badge tone="danger">
            <CrossIcon className="size-3" />
            {t('transcript.rejected')}
          </Badge>
        )}
      </div>

      <div className="flex flex-col gap-2 border-t border-line px-3 py-2.5">
        {item.message !== null && <p className="text-sm text-subtle">{item.message}</p>}

        <div>
          <button
            type="button"
            data-testid="approval-toggle-arguments"
            onClick={() => setArgsOpen(!argsOpen)}
            className="flex items-center gap-1 text-xs font-semibold tracking-wide text-subtle uppercase"
          >
            <ChevronIcon className={cx('size-3 transition-transform', argsOpen && 'rotate-90')} />
            {t('transcript.arguments')}
          </button>
          {argsOpen && (
            <div className="mt-1">
              <Section label="" body={item.args} empty={t('transcript.noArguments')} />
            </div>
          )}
        </div>

        {item.decided === null && onDecide !== undefined && (
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              data-testid="approval-approve"
              onClick={() => onDecide(item.requestId, true, remember)}
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-transparent bg-accent px-3 text-sm font-medium text-accent-fg hover:bg-accent-hover"
            >
              <CheckIcon className="size-3" />
              {t('transcript.approve')}
            </button>

            <button
              type="button"
              data-testid="approval-reject"
              onClick={() => onDecide(item.requestId, false, false)}
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-line px-3 text-sm font-medium text-danger hover:bg-danger-soft"
            >
              <CrossIcon className="size-3" />
              {t('transcript.reject')}
            </button>

            <label className="flex items-center gap-1.5 text-xs text-muted">
              <input
                type="checkbox"
                checked={remember}
                onChange={(event) => setRemember(event.target.checked)}
              />
              {t('transcript.rememberDecision')}
            </label>
          </div>
        )}
      </div>
    </div>
  );
}

/** What the user did with a tool's request for input; the words the server's API takes. */
export type InputAction = 'Accept' | 'Decline' | 'Cancel';

type FieldValue = string | boolean | string[];

/**
 * A tool's request for input.
 *
 * An MCP server asked the user for information in the middle of a tool call. The run
 * has already ended: the answer is carried as the input of the next turn, and the
 * tool call then runs again with it. The card says where the information goes, because
 * the server belongs to a third party and the user is about to hand it something.
 */
function InputCard({
  item,
  onAnswer,
}: {
  item: Extract<TranscriptItem, { kind: 'input' }>;
  onAnswer?: (requestId: string, action: InputAction, content: Record<string, unknown> | null) => void;
}): ReactNode {
  const t = useT();
  const [values, setValues] = useState<Record<string, FieldValue>>({});
  const open = item.answered === null && onAnswer !== undefined;

  const set = (name: string, value: FieldValue): void => setValues((current) => ({ ...current, [name]: value }));

  const missing = item.fields.some((field) => field.required && isBlank(field, values[field.name]));

  const answer = (action: InputAction, content: Record<string, unknown> | null): void =>
    onAnswer?.(item.requestId, action, content);

  const submit = (): void => {
    if (missing) {
      return;
    }

    answer('Accept', contentOf(item.fields, values));
  };

  return (
    <div
      data-testid="input-card"
      className="overflow-hidden rounded-md border border-line bg-panel"
      style={{ borderLeft: '2px solid var(--tracon-warn)' }}
    >
      <div className="flex items-center gap-2 px-3 py-2">
        <span className="flex flex-col">
          <span className="text-sm font-medium">{item.serverName}</span>
          <span className="font-mono text-2xs text-subtle">{item.toolName}</span>
        </span>
        {item.answered === null ? (
          <Badge tone="warn">{t('transcript.inputRequested')}</Badge>
        ) : item.answered === 'accepted' ? (
          <Badge tone="success">
            <CheckIcon className="size-3" />
            {t('transcript.inputAccepted')}
          </Badge>
        ) : (
          <Badge tone="danger">
            <CrossIcon className="size-3" />
            {item.answered === 'declined' ? t('transcript.inputDeclined') : t('transcript.inputCancelled')}
          </Badge>
        )}
      </div>

      <form
        className="flex flex-col gap-2.5 border-t border-line px-3 py-2.5"
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <p className="text-sm whitespace-pre-wrap">{item.message}</p>
        <p className="text-xs text-subtle">{t('transcript.inputGoesTo', { server: item.serverName })}</p>

        {item.fields.length > 0 && (
          <div className="grid gap-2.5 sm:grid-cols-2">
            {item.fields.map((field) => (
              <InputFieldControl
                key={field.name}
                field={field}
                value={values[field.name]}
                disabled={!open}
                onChange={(value) => set(field.name, value)}
              />
            ))}
          </div>
        )}

        {open && (
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="submit"
              data-testid="input-submit"
              disabled={missing}
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-transparent bg-accent px-3 text-sm font-medium text-accent-fg hover:bg-accent-hover disabled:opacity-50"
            >
              <CheckIcon className="size-3" />
              {t('transcript.inputSubmit')}
            </button>

            <button
              type="button"
              data-testid="input-decline"
              onClick={() => answer('Decline', null)}
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-line px-3 text-sm font-medium text-danger hover:bg-danger-soft"
            >
              <CrossIcon className="size-3" />
              {t('transcript.inputDecline')}
            </button>

            <button
              type="button"
              data-testid="input-cancel"
              onClick={() => answer('Cancel', null)}
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-line px-3 text-sm font-medium text-muted hover:bg-raised"
            >
              {t('transcript.inputCancel')}
            </button>
          </div>
        )}
      </form>
    </div>
  );
}

function InputFieldControl({
  field,
  value,
  disabled,
  onChange,
}: {
  field: InputField;
  value: FieldValue | undefined;
  disabled: boolean;
  onChange: (value: FieldValue) => void;
}): ReactNode {
  const t = useT();
  const testId = `input-field-${field.name}`;

  if (field.kind === 'boolean') {
    return (
      <Field label={field.label} hint={field.description ?? undefined}>
        <input
          type="checkbox"
          data-testid={testId}
          checked={value === true}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked)}
          className="size-4"
        />
      </Field>
    );
  }

  if (field.kind === 'choice') {
    return (
      <Field label={field.label} hint={field.description ?? undefined} required={field.required}>
        {(ids) => (
          <Select
            {...ids}
            testId={testId}
            value={typeof value === 'string' ? value : ''}
            disabled={disabled}
            onChange={onChange}
          >
            <option value="">{t('transcript.inputChoose')}</option>
            {field.options.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </Select>
        )}
      </Field>
    );
  }

  if (field.kind === 'choices') {
    const chosen = Array.isArray(value) ? value : [];

    return (
      <fieldset data-testid={testId} className="flex flex-col gap-1">
        <legend className="mb-1 flex items-baseline gap-1.5 text-xs font-medium text-muted">
          {field.label}
          {field.required && <span className="text-danger">*</span>}
        </legend>
        {field.options.map((option) => (
          <label key={option.value} className="flex items-center gap-1.5 text-sm">
            <input
              type="checkbox"
              checked={chosen.includes(option.value)}
              disabled={disabled}
              onChange={(event) =>
                onChange(event.target.checked ? [...chosen, option.value] : chosen.filter((entry) => entry !== option.value))
              }
            />
            {option.label}
          </label>
        ))}
        {field.description !== null && <span className="text-xs text-subtle">{field.description}</span>}
      </fieldset>
    );
  }

  const numeric = field.kind === 'number' || field.kind === 'integer';

  return (
    <Field label={field.label} hint={field.description ?? undefined} required={field.required}>
      {(ids) => (
        <TextInput
          {...ids}
          data-testid={testId}
          type={numeric ? 'number' : 'text'}
          step={field.kind === 'integer' ? 1 : numeric ? 'any' : undefined}
          min={field.minimum ?? undefined}
          max={field.maximum ?? undefined}
          minLength={field.minLength ?? undefined}
          maxLength={field.maxLength ?? undefined}
          required={field.required}
          disabled={disabled}
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  );
}

/** Whether a field still has nothing the server could read as a value. */
function isBlank(field: InputField, value: FieldValue | undefined): boolean {
  switch (field.kind) {
    case 'boolean':
      // An unchecked box is a value (false); a required boolean is never "missing".
      return false;

    case 'choices':
      return !Array.isArray(value) || value.length === 0;

    default:
      return typeof value !== 'string' || value.trim().length === 0;
  }
}

/**
 * The values to send: only fields that have one, numbers as numbers, a checkbox as
 * a boolean. An optional field left empty is left out, not sent empty.
 */
function contentOf(fields: readonly InputField[], values: Record<string, FieldValue>): Record<string, unknown> {
  const content: Record<string, unknown> = {};

  for (const field of fields) {
    const value = values[field.name];

    if (field.kind === 'boolean') {
      content[field.name] = value === true;
    } else if (!isBlank(field, value)) {
      content[field.name] =
        field.kind === 'number' || field.kind === 'integer' ? Number(value) : field.kind === 'choices' ? value : String(value).trim();
    }
  }

  return content;
}

function ToolCard({ item }: { item: Extract<TranscriptItem, { kind: 'tool' }> }): ReactNode {
  const t = useT();
  // A finished call is usually noise once you have read it; a running or failed
  // one is exactly what you opened the screen for.
  const [open, setOpen] = useState(item.state !== 'ok');

  return (
    <div
      data-testid="tool-card"
      className="overflow-hidden rounded-md border border-line bg-panel"
      style={{ borderLeft: '2px solid var(--tracon-danger)' }}
    >
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 px-3 py-2 text-left"
      >
        <ChevronIcon className={cx('size-3 shrink-0 text-subtle transition-transform', open && 'rotate-90')} />
        <span className="font-mono text-sm font-medium">{item.name}</span>
        <ToolState state={item.state} />
      </button>

      {open && (
        <div className="flex flex-col gap-2 border-t border-line px-3 py-2.5">
          <Section label={t('transcript.arguments')} body={item.args} empty={t('transcript.noArguments')} />
          {item.error === null ? (
            <Section label={t('common.result')} body={item.result} empty={t('transcript.waitingResult')} />
          ) : (
            <Section label={t('common.error')} body={item.error} empty="" tone="danger" />
          )}
        </div>
      )}
    </div>
  );
}

function ToolState({ state }: { state: 'running' | 'ok' | 'failed' }): ReactNode {
  const t = useT();

  if (state === 'running') {
    return (
      <Badge tone="info">
        <SpinnerIcon className="size-3" />
        {t('runs.status.running')}
      </Badge>
    );
  }

  if (state === 'failed') {
    return (
      <Badge tone="danger">
        <CrossIcon className="size-3" />
        {t('runs.status.failed')}
      </Badge>
    );
  }

  return (
    <Badge tone="success">
      <CheckIcon className="size-3" />
      {t('transcript.done')}
    </Badge>
  );
}

function Section({
  label,
  body,
  empty,
  tone,
}: {
  label: string;
  body: string | null;
  empty: string;
  tone?: 'danger';
}): ReactNode {
  return (
    <div>
      {label.length > 0 && (
        <span className="mb-1 block text-xs font-semibold tracking-wide text-subtle uppercase">
          {label}
        </span>
      )}
      {body === null || body.length === 0 ? (
        <span className={cx('text-sm', tone === 'danger' ? 'text-danger' : 'text-subtle')}>
          {empty}
        </span>
      ) : (
        <CodeBlock code={prettyJson(body)} maxHeight="max-h-64" />
      )}
    </div>
  );
}
