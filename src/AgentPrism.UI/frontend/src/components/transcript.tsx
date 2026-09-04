import { useState, type ReactNode } from 'react';
import { prettyJson } from '../lib/format';
import { useT } from '../lib/i18n';
import type { TranscriptItem } from '../lib/transcript';
import { Badge, CodeBlock, cx } from './ui';
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
}: {
  items: readonly TranscriptItem[];
  streaming?: boolean;
  /**
   * Called when the operator answers a pending approval. Absent on read-only
   * views (a recorded run), where the decision has already been made or the
   * turn can no longer be continued.
   */
  onDecide?: (requestId: string, approved: boolean, remember: boolean) => void;
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
                  'text-[13px] leading-relaxed whitespace-pre-wrap',
                  streaming === true && isLast && 'ap-stream-caret',
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
                className="rounded-md border border-line bg-danger-soft px-3 py-2 text-[12px] text-danger"
              >
                {item.message}
              </div>
            );

          case 'compaction':
            return (
              <div
                key={item.id}
                className="rounded-md border border-line bg-accent-soft px-3 py-2 text-[12px] text-accent"
                title={item.detail ?? undefined}
              >
                {item.message}
              </div>
            );

          case 'approval':
            return <ApprovalCard key={item.id} item={item} onDecide={onDecide} />;

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
        className="flex w-full items-center gap-1.5 px-3 py-1.5 text-left text-[12px] text-muted"
      >
        <ChevronIcon className={cx('size-3 transition-transform', open && 'rotate-90')} />
        {t('transcript.reasoning')}
      </button>
      {open && (
        <p className="px-3 pb-3 text-[12px] leading-relaxed whitespace-pre-wrap text-muted">{text}</p>
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
      style={{ borderLeft: '2px solid var(--ap-amber)' }}
    >
      <div className="flex items-center gap-2 px-3 py-2">
        {item.entityName === null ? (
          <span className="font-mono text-[12px] font-medium">{item.name}</span>
        ) : (
          <span className="flex flex-col">
            <span className="text-[12px] font-medium">{item.entityName}</span>
            <span className="font-mono text-[10px] text-subtle">{item.name}</span>
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
        {item.message !== null && <p className="text-[12px] text-subtle">{item.message}</p>}

        <div>
          <button
            type="button"
            data-testid="approval-toggle-arguments"
            onClick={() => setArgsOpen(!argsOpen)}
            className="flex items-center gap-1 text-[11px] font-semibold tracking-wide text-subtle uppercase"
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
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-transparent bg-accent px-3 text-[12px] font-medium text-accent-fg hover:bg-accent-hover"
            >
              <CheckIcon className="size-3" />
              {t('transcript.approve')}
            </button>

            <button
              type="button"
              data-testid="approval-reject"
              onClick={() => onDecide(item.requestId, false, false)}
              className="inline-flex h-7 items-center gap-1.5 rounded-md border border-line px-3 text-[12px] font-medium text-danger hover:bg-danger-soft"
            >
              <CrossIcon className="size-3" />
              {t('transcript.reject')}
            </button>

            <label className="flex items-center gap-1.5 text-[11px] text-muted">
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

function ToolCard({ item }: { item: Extract<TranscriptItem, { kind: 'tool' }> }): ReactNode {
  const t = useT();
  // A finished call is usually noise once you have read it; a running or failed
  // one is exactly what you opened the screen for.
  const [open, setOpen] = useState(item.state !== 'ok');

  return (
    <div
      data-testid="tool-card"
      className="overflow-hidden rounded-md border border-line bg-panel"
      style={{ borderLeft: '2px solid var(--ap-rose)' }}
    >
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 px-3 py-2 text-left"
      >
        <ChevronIcon className={cx('size-3 shrink-0 text-subtle transition-transform', open && 'rotate-90')} />
        <span className="font-mono text-[12px] font-medium">{item.name}</span>
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
        <span className="mb-1 block text-[11px] font-semibold tracking-wide text-subtle uppercase">
          {label}
        </span>
      )}
      {body === null || body.length === 0 ? (
        <span className={cx('text-[12px]', tone === 'danger' ? 'text-danger' : 'text-subtle')}>
          {empty}
        </span>
      ) : (
        <CodeBlock code={prettyJson(body)} maxHeight="max-h-64" />
      )}
    </div>
  );
}
